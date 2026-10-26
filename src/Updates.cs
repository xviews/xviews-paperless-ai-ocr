// SPDX-License-Identifier: MIT
// Copyright (C) 2026 Xviews


using System.Globalization;
using Avalonia.Controls;

namespace XviewsPaperlessAiOcr;

/// <summary>
/// Update-Status für Menü und Info-Fenster. Fragt GitHub von selbst höchstens alle 20 Stunden,
/// installiert aber nur nach Rückfrage.
/// </summary>
sealed class Updates
{
    readonly Settings settings;
    readonly JobStore store;

    public UpdateInfo? Available { get; private set; }
    public string Status { get; private set; } = T("Noch nicht geprüft.", "Not checked yet.");
    public bool Busy { get; private set; }

    /// <summary>Status oder verfügbare Version hat sich geändert (immer im UI-Thread).</summary>
    public event Action? Changed;

    public Updates(Settings settings, JobStore store)
    {
        this.settings = settings;
        this.store = store;
        Apply(settings.LatestVersion, settings.LatestUrl);
    }

    /// <summary>Beim Start: nach einem Update aufräumen, dann höchstens einmal am Tag bei GitHub nachfragen.</summary>
    public async Task CheckOnStartAsync()
    {
        await UpdateChecker.CleanupAsync();
        if (DateTime.TryParse(settings.LastUpdateCheck, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime last)
            && DateTime.Now - last < TimeSpan.FromHours(20)) return;
        await CheckAsync();
    }

    public async Task CheckAsync()
    {
        if (Busy) return;
        SetBusy(T("Suche nach Updates …", "Checking for updates …"));
        UpdateInfo? latest = await UpdateChecker.GetLatestAsync();
        Busy = false;
        if (latest is null)
        {
            Status = T("Keine Verbindung zu GitHub. Später noch einmal versuchen.", "No connection to GitHub. Please try again later.");
            Changed?.Invoke();
            return;
        }
        settings.LastUpdateCheck = DateTime.Now.ToString("s", CultureInfo.InvariantCulture);
        settings.LatestVersion = latest.VersionText;
        settings.LatestUrl = latest.Url;
        settings.Save();
        Apply(latest.VersionText, latest.Url);
    }

    void Apply(string version, string url)
    {
        Version? known = Version.TryParse(version, out Version? v) ? v : null;
        Available = known is not null && known > UpdateChecker.Current
            ? new UpdateInfo(known, url == "" ? UpdateChecker.ReleasesPage : url, []) : null;
        string checkedAt = DateTime.TryParse(settings.LastUpdateCheck, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime t)
            ? T($" (geprüft {t:dd.MM.yyyy HH:mm})", $" (checked {t:yyyy-MM-dd HH:mm})") : "";
        Status = Available is { } u ? T($"Version {u.VersionText} ist verfügbar{checkedAt}.", $"Version {u.VersionText} is available{checkedAt}.")
            : known is not null ? T($"Du hast die neueste Version{checkedAt}.", $"You have the latest version{checkedAt}.")
            : T("Noch nicht geprüft.", "Not checked yet.");
        Changed?.Invoke();
    }

    void SetBusy(string status)
    {
        Busy = true;
        Status = status;
        Changed?.Invoke();
    }

    public async Task InstallAsync(Window owner)
    {
        if (Busy) return;
        if (store.Active > 0)
        {
            await Dialogs.InfoAsync(owner, T("Bitte kurz warten", "Please wait a moment"), T("Gerade läuft eine Texterkennung. Bitte danach aktualisieren.", "Text recognition is running right now. Please update afterwards."));
            return;
        }
        SetBusy(T("Suche nach Updates …", "Checking for updates …"));
        UpdateInfo? latest = await UpdateChecker.GetLatestAsync();
        Busy = false;
        if (latest is null || latest.Version <= UpdateChecker.Current)
        {
            Apply(latest?.VersionText ?? settings.LatestVersion, latest?.Url ?? "");
            if (latest is null) OpenUrl(UpdateChecker.ReleasesPage);
            return;
        }
        Apply(latest.VersionText, latest.Url);
        if (latest.Exe is not { Sha256: not null } asset)
        {
            if (await Dialogs.AskAsync(owner, "Update", T($"Version {latest.VersionText} kann nicht direkt installiert werden. Download-Seite auf GitHub öffnen?", $"Version {latest.VersionText} cannot be installed directly. Open the download page on GitHub?"), OpenDownloadPage))
                OpenUrl(latest.Url);
            return;
        }
        if (!await Dialogs.AskAsync(owner, T($"Version {latest.VersionText} installieren?", $"Install version {latest.VersionText}?"),
            T($"Das Tool lädt „{asset.Name}“ ({asset.Size / 1048576.0:0} MB) von GitHub, prüft die Prüfsumme und startet danach neu. Einstellungen und API-Schlüssel bleiben erhalten.",
              $"The tool downloads “{asset.Name}” ({asset.Size / 1048576.0:0} MB) from GitHub, verifies the checksum and then restarts. Settings and API key are kept."), T("Installieren", "Install"))) return;

        SetBusy(T("Update wird geladen …", "Downloading update …"));
        try
        {
            await UpdateChecker.InstallAsync(asset, new Progress<double>(p =>
            {
                Status = T("Update wird geladen …", "Downloading update …") + $" {p:0} %";
                Changed?.Invoke();
            }));
            UpdateChecker.Restart();
            App.Quit();
        }
        catch (Exception ex)
        {
            Log.Write($"Update fehlgeschlagen: {ex}");
            Busy = false;
            Apply(latest.VersionText, latest.Url);
            if (await Dialogs.AskAsync(owner, T("Update fehlgeschlagen", "Update failed"),
                    ex.Message + T("\n\nDie bisherige Version bleibt erhalten. Stattdessen die Download-Seite öffnen?",
                                   "\n\nThe current version is kept. Open the download page instead?"), OpenDownloadPage))
                OpenUrl(latest.Url);
        }
    }

    static string OpenDownloadPage => T("Download-Seite öffnen", "Open download page");

    public static void OpenUrl(string url) => Ui.OpenUrl(url);
}
