// SPDX-License-Identifier: MIT
// Copyright (C) 2026 Xviews

using System.Text.Json;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace XviewsPaperlessAiOcr;

/// <summary>
/// Gibt sich gegenüber Paperless-ngx als "Azure AI Document Intelligence" (Modell prebuilt-read,
/// API 2024-11-30) aus und erledigt die OCR lokal mit einem Vision-Modell (Ollama oder OpenAI-kompatibler Server).
/// </summary>
static class Program
{
    public const string Name = "Xviews Paperless AI-OCR";
    const string MutexName = @"Local\XviewsPaperlessAiOcr";
    const string ApiVersion = "2024-11-30";

    static Mutex mutex = null!;
    static Signal showSignal = null!, exitSignal = null!;
    static bool ownsMutex;
    static WebApplication? web;
    static Tray? tray;

    [STAThread]
    static int Main(string[] args)
    {
        using var m = mutex = new Mutex(false, MutexName);
        using var show = showSignal = new Signal("XviewsPaperlessAiOcr.Show");
        using var exit = exitSignal = new Signal("XviewsPaperlessAiOcr.Exit");

        bool uninstall = args.Contains("--deinstallieren");
        bool askInstall = args.Contains("--installieren");
        if (!uninstall && !askInstall && !Installation.RunningInstalled && !args.Contains("--update") && !args.Contains("--autostart"))
        {
            // Installiert ist genau diese oder eine neuere Version: die nehmen.
            // Gleiche Versionsnummer, aber anderer Build: aktualisieren anbieten.
            Version own = UpdateChecker.Current;
            if (Installation.InstalledVersion() is { } v && (v > own || (v == own && Installation.InstalledIsSame())))
            {
                Installation.StartInstalled(args);
                return 0;
            }
            askInstall = true;
        }
        if (!uninstall && !askInstall)
        {
            // Nach einem Update wartet die neue Version, bis die alte beendet ist und den Port freigegeben hat
            if (!Acquire(args.Contains("--update") ? 20000 : 0))
            {
                // Läuft schon: dort das Info-Fenster öffnen (z. B. nach Klick auf den Startmenü-Eintrag)
                showSignal.Set();
                return 0;
            }
            if (Installation.RunningInstalled) Installation.Refresh();
        }

        App.Start = desktop =>
        {
            // Symbol entfernen, solange der UI-Thread noch läuft
            desktop.Exit += (_, _) =>
            {
                tray?.Dispose();
                tray = null;
            };
            if (uninstall) ShowUninstall();
            else if (askInstall) _ = AskInstallAsync(args);
            else StartTool(args);
        };
        int code;
        try
        {
            code = AppBuilder.Configure<App>().UsePlatformDetect().LogToTrace().StartWithClassicDesktopLifetime(args);
        }
        finally
        {
            if (web is not null)
            {
                // Auf einem Hintergrund-Thread, sonst warten Fortsetzungen auf den beendeten UI-Thread
                Task.Run(() => web.StopAsync()).Wait(TimeSpan.FromSeconds(10));
                Log.Write("Beendet");
            }
            if (ownsMutex) mutex.ReleaseMutex();
        }
        return code;
    }

    static bool Acquire(int timeoutMs)
    {
        try { ownsMutex = mutex.WaitOne(timeoutMs); }
        catch (AbandonedMutexException) { ownsMutex = true; }
        return ownsMutex;
    }

    /// <summary>Webserver und Symbol im Infobereich starten (normaler Betrieb).</summary>
    static void StartTool(string[] args)
    {
        var settings = Settings.Load();
        var store = new JobStore(settings);
        web = BuildWebApp(settings, store);
        try
        {
            web.StartAsync().GetAwaiter().GetResult();
        }
        catch (IOException e)
        {
            web = null;
            Log.Write($"Start fehlgeschlagen: {e.Message}");
            Notification.Show(T($"Port {settings.Port} ist schon belegt", $"Port {settings.Port} is already in use"),
                T("Läuft das Tool bereits oder ein anderes Programm auf diesem Port? Details im Protokoll.", "Is the tool already running, or another program on this port? Details in the log."), error: true, seconds: 12);
            DispatcherTimer.RunOnce(() => App.Quit(1), TimeSpan.FromSeconds(12));
            return;
        }
        Log.Write($"Version {UpdateChecker.Current} gestartet auf Port {settings.Port}, Modell {settings.Model}" +
                  (Installation.RunningInstalled ? " (installiert)" : " (ohne Installation)"));

        tray = new Tray(settings, store, new Updates(settings, store),
            showInfoNow: settings.FirstStart || (!args.Contains("--autostart") && !args.Contains("--update")));
        // Ein zweiter Start bittet dieses Tool, sein Fenster zu zeigen; Installer und Deinstaller bitten es, sich zu beenden
        showSignal.Listen(() => Dispatcher.UIThread.Post(() => tray?.ShowInfo()));
        exitSignal.Listen(() => Dispatcher.UIThread.Post(() => App.Quit()));
        // macOS startet die App beim zweiten Öffnen nicht neu, sondern meldet es der laufenden
        if (Application.Current?.TryGetFeature<IActivatableLifetime>() is { } activatable)
            activatable.Activated += (_, e) =>
            {
                if (e.Kind == ActivationKind.Reopen) tray?.ShowInfo();
            };
    }

    /// <summary>Bittet ein laufendes Tool, sich zu beenden, und wartet bis zu 15 Sekunden darauf.</summary>
    static Task<bool> StopRunningAsync() => Task.Run(() =>
    {
        bool got;
        try { got = mutex.WaitOne(0); }
        catch (AbandonedMutexException) { got = true; }
        if (!got)
        {
            exitSignal.Set();
            try { got = mutex.WaitOne(15000); }
            catch (AbandonedMutexException) { got = true; }
        }
        if (got) mutex.ReleaseMutex();
        return got;
    });

    static async Task AskInstallAsync(string[] args)
    {
        var window = new InstallWindow(Installation.InstalledVersion());
        window.Show();
        switch (await window.Choice)
        {
            case InstallChoice.Install:
                string? error = null;
                if (!await StopRunningAsync())
                    error = StillRunning;
                else
                    try { await Task.Run(Installation.Install); }
                    catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
                    {
                        error = e.Message;
                    }
                if (error is not null)
                {
                    Log.Write($"Installation fehlgeschlagen: {error}");
                    Notification.Show(T("Installation fehlgeschlagen", "Installation failed"), error, error: true, seconds: 15);
                    DispatcherTimer.RunOnce(() => App.Quit(1), TimeSpan.FromSeconds(15));
                    return;
                }
                Installation.StartInstalled(args);
                App.Quit();
                break;
            case InstallChoice.Portable:
                // Läuft schon ein Tool, dessen Fenster zeigen
                if (!Acquire(0))
                {
                    showSignal.Set();
                    App.Quit();
                    return;
                }
                StartTool(args);
                break;
            default:
                App.Quit();
                break;
        }
    }

    static string StillRunning => T($"Das laufende Tool hat sich nicht beendet. Bitte über das Symbol {Tray.Area} beenden und erneut versuchen.",
                                      $"The running tool did not quit. Please quit it via the icon {Tray.Area} and try again.");

    static void ShowUninstall()
    {
        var window = new UninstallWindow(async deleteData =>
        {
            if (!await StopRunningAsync())
                return StillRunning;
            try
            {
                await Task.Run(() => Installation.Uninstall(deleteData));
                return null;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                return e.Message;
            }
        });
        window.Closed += (_, _) => App.Quit();
        window.Show();
    }

    static WebApplication BuildWebApp(Settings settings, JobStore store)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls($"http://0.0.0.0:{settings.Port}");
        builder.WebHost.ConfigureKestrel(k => k.Limits.MaxRequestBodySize = 600L * 1024 * 1024);
        var app = builder.Build();

        app.MapGet("/", () => Results.Text(T($"{Name} {UpdateChecker.Current} läuft, {store.Active} Auftrag/Aufträge aktiv.\n",
                                             $"{Name} {UpdateChecker.Current} is running, {store.Active} job(s) active.\n") +
                                           T("Quellcode", "Source code") + $": {InfoWindow.ProjectUrl}"));

        // Alle Azure-Aufrufe liegen unter /documentintelligence/documentModels/...
        app.Map("/documentintelligence/documentModels/{**rest}", async (HttpContext ctx, string rest) =>
        {
            string from = ctx.Connection.RemoteIpAddress?.MapToIPv4().ToString() ?? "?";
            if (ctx.Request.Headers["Ocp-Apim-Subscription-Key"] != settings.ApiKey)
            {
                store.RejectWrongKey(from);
                return Error(401, "401", "Access denied due to invalid subscription key.");
            }

            // POST prebuilt-read:analyze
            if (HttpMethods.IsPost(ctx.Request.Method) && rest.EndsWith(":analyze"))
                return await AnalyzeAsync(ctx, rest[..^":analyze".Length], store, from);

            var parts = rest.Split('/');
            // GET {model}/analyzeResults/{id}[/pdf]
            if (HttpMethods.IsGet(ctx.Request.Method) && parts.Length >= 3 && parts[1] == "analyzeResults")
            {
                var job = store.Get(parts[2]);
                if (job is null) return Error(404, "NotFound", "Result not found.");
                if (parts.Length == 4 && parts[3] == "pdf")
                    return job.Pdf is null
                        ? Error(404, "NotFound", "PDF not available.")
                        : Results.File(job.Pdf, "application/pdf");
                return Results.Json(StatusBody(job, parts[0]), statusCode: 200);
            }
            return Error(404, "NotFound", $"Unsupported request {ctx.Request.Method} {rest}");
        });

        return app;
    }

    static async Task<IResult> AnalyzeAsync(HttpContext ctx, string modelId, JobStore store, string from)
    {
        byte[] document;
        try
        {
            using var json = await JsonDocument.ParseAsync(ctx.Request.Body);
            if (!json.RootElement.TryGetProperty("base64Source", out var b64))
                return Error(400, "InvalidRequest", "Only base64Source is supported.");
            document = b64.GetBytesFromBase64();
        }
        catch (Exception e)
        {
            return Error(400, "InvalidRequest", e.Message);
        }

        var job = store.Start(document, from);
        var location = $"{ctx.Request.Scheme}://{ctx.Request.Host}/documentintelligence/documentModels/" +
                       $"{modelId}/analyzeResults/{job.Id}?api-version={ApiVersion}";
        ctx.Response.Headers["Operation-Location"] = location;
        ctx.Response.Headers["Retry-After"] = "2";
        ctx.Response.Headers["apim-request-id"] = job.Id;
        return Results.StatusCode(202);
    }

    static object StatusBody(OcrJob job, string modelId)
    {
        string status = job.Status switch
        {
            JobStatus.NotStarted => "notStarted",
            JobStatus.Running => "running",
            JobStatus.Succeeded => "succeeded",
            _ => "failed",
        };
        var body = new Dictionary<string, object?>
        {
            ["status"] = status,
            ["createdDateTime"] = job.Created.ToString("o"),
            ["lastUpdatedDateTime"] = job.Updated.ToString("o"),
        };
        if (job.Status == JobStatus.Failed)
            body["error"] = new { code = "InternalServerError", message = job.Error ?? "OCR failed" };
        if (job.Status == JobStatus.Succeeded)
            body["analyzeResult"] = new
            {
                apiVersion = ApiVersion,
                modelId,
                stringIndexType = "textElements",
                content = job.Content,
                contentFormat = "text",
                pages = job.Pages.Select((p, i) => new
                {
                    pageNumber = i + 1,
                    angle = 0.0,
                    width = p.W,
                    height = p.H,
                    unit = "inch",
                    spans = new[] { new { offset = p.Offset, length = p.Length } },
                    words = Array.Empty<object>(),
                    lines = Array.Empty<object>(),
                }),
                paragraphs = Array.Empty<object>(),
                styles = Array.Empty<object>(),
            };
        return body;
    }

    static IResult Error(int status, string code, string message)
        => Results.Json(new { error = new { code, message } }, statusCode: status);
}
