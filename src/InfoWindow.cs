// SPDX-License-Identifier: MIT
// Copyright (C) 2026 Xviews

using System.Net.NetworkInformation;
using System.Net.Sockets;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace XviewsPaperlessAiOcr;

public sealed record CreditLine(string Name, string Detail, string Url);

/// <summary>Info-Fenster: Einrichtung in Paperless, Verhalten, Updates, Installation, Datenordner, Hinweise und Komponenten.</summary>
sealed class InfoWindow : Window
{
    public const string ProjectUrl = "https://github.com/xviews/xviews-paperless-ai-ocr";

    static readonly CreditLine[] Credits =
    [
        new("PdfPig 0.1.16", T("© UglyToad und Mitwirkende, Apache-Lizenz 2.0. Baut das durchsuchbare PDF",
                               "© UglyToad and contributors, Apache License 2.0. Builds the searchable PDF"), "https://github.com/UglyToad/PdfPig"),
        new("Avalonia 12.1.4", T("© AvaloniaUI OÜ, MIT-Lizenz. Fenster und Symbol im Infobereich",
                                 "© AvaloniaUI OÜ, MIT license. Windows and tray icon"), "https://github.com/AvaloniaUI/Avalonia"),
        new("FluentAvalonia 3.1.0", T("© amwx, MIT-Lizenz. Windows-11-Design", "© amwx, MIT license. Windows 11 look"), "https://github.com/amwx/FluentAvalonia"),
        new("SkiaSharp / HarfBuzzSharp", T("© Microsoft, Google, HarfBuzz-Autoren, MIT-, BSD- und HarfBuzz-Lizenz. Grafik und Schrift",
                                           "© Microsoft, Google, HarfBuzz authors, MIT, BSD and HarfBuzz licenses. Graphics and text"), "https://github.com/mono/SkiaSharp"),
        new(".NET / ASP.NET Core", T("© .NET Foundation, MIT-Lizenz. Laufzeit und Webserver", "© .NET Foundation, MIT license. Runtime and web server"), "https://github.com/dotnet/runtime"),
#if MAC
        new("PDFium", T("© The PDFium Authors, BSD-3-Clause- und Apache-Lizenz 2.0. Macht PDF-Seiten zu Bildern",
                        "© The PDFium Authors, BSD 3-Clause and Apache License 2.0. Turns PDF pages into images"), "https://github.com/bblanchon/pdfium-binaries"),
#endif
    ];

    static string Terms => T(
        "Xviews Paperless AI-OCR ist freie Software unter der MIT-Lizenz: Du darfst es nutzen, verändern und weitergeben, " +
        "solange der Copyright-Hinweis und der Lizenztext erhalten bleiben.\n\n" +
        "Der vollständige Quellcode steht auf GitHub.\n\n" +
        "Das Programm wird ohne jede Gewähr bereitgestellt, so wie es ist.\n\n" +
        "Die Lizenz gewährt keine Rechte am Namen „Xviews“ und am Xviews-Logo. " +
        "Die Rechte an eingebundenen Komponenten liegen bei ihren jeweiligen Urhebern. Lizenztexte unten unter „Lizenzen anzeigen“.",
        "Xviews Paperless AI-OCR is free software under the MIT license: you may use, modify and share it " +
        "as long as the copyright notice and the license text are kept.\n\n" +
        "The full source code is on GitHub.\n\n" +
        "The program is provided as is, without any warranty.\n\n" +
        "The license grants no rights to the name \"Xviews\" or the Xviews logo. " +
        "The rights to included components belong to their respective authors. License texts below under \"Show licenses\".");

    readonly Settings settings;
    readonly Updates updates;
    readonly JobStore store;
    readonly TextBlock updateStatus = Ui.Para("");
    readonly TextBlock serverStatus = Ui.Para(T("KI-Server wird geprüft …", "Checking AI server …"));
    readonly TextBlock paperlessStatus = Ui.Para("");
    readonly Button checkButton, installButton, pageButton;
    readonly ComboBox modelBox = new() { MinWidth = 400 };
    bool loadingModels;

    public InfoWindow(Settings settings, Updates updates, JobStore store)
    {
        this.settings = settings;
        this.updates = updates;
        this.store = store;

        Title = Program.Name;
        Icon = App.Icon;
        Width = 740;
        Height = 900;
        MinWidth = 600;
        MinHeight = 400;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Classes.Add("glas");

        checkButton = Ui.Button(T("Jetzt suchen", "Check now"), updates.CheckAsync);
        installButton = Ui.Button(T("Jetzt aktualisieren", "Update now"), () => updates.InstallAsync(this), accent: true);
        pageButton = Ui.Button(T("Download-Seite", "Download page"), () => Ui.OpenUrl(updates.Available?.Url ?? UpdateChecker.ReleasesPage));

        var cards = new StackPanel { Spacing = 12, Margin = new Thickness(16) };
        cards.Children.Add(HeaderCard());
        cards.Children.Add(SetupCard());
        // Die Erklärungen stehen im Tooltip, damit die Karte kurz bleibt
        cards.Children.Add(Ui.Card(T("Verhalten", "Behaviour"),
            Ui.Check(Autostart.Label, Autostart.Enabled, on => Autostart.Enabled = on,
                T($"Das Tool wartet dann unauffällig {Tray.Area} und braucht kaum Arbeitsspeicher. Ollama startet trotzdem erst, wenn Paperless ein Dokument schickt.",
                  $"The tool then waits quietly {Tray.Area} and uses hardly any memory. Ollama still only starts when Paperless sends a document.")),
            Ui.Check(T("Benachrichtigen, wenn ein Dokument fertig ist", "Notify when a document is done"), settings.NotifyWhenDone, on => { settings.NotifyWhenDone = on; settings.Save(); },
                T("Kurze Meldung unten rechts mit Seitenzahl und Dauer.", "Short message at the bottom right with page count and duration.")),
            Ui.Check(T("Fehler melden", "Report errors"), settings.NotifyErrors, on => { settings.NotifyErrors = on; settings.Save(); },
                T("Meldung, wenn die Texterkennung fehlschlägt oder Paperless einen falschen API-Schlüssel schickt. Ausgeschaltet stehen Fehler nur noch im Protokoll und im Statusfenster.",
                  "Message when text recognition fails or Paperless sends a wrong API key. When off, errors only show up in the log and in the status window.")),
            Section("Ollama"),
            Ui.Check(T("Bei Bedarf starten", "Start when needed"), settings.StartOllamaOnDemand, on => { settings.StartOllamaOnDemand = on; settings.Save(); _ = RefreshServerAsync(); },
                T("Startet Ollama, sobald Paperless ein Dokument schickt und Ollama noch nicht läuft. Ausgeschaltet musst du Ollama selbst starten.",
                  "Starts Ollama as soon as Paperless sends a document and Ollama is not running yet. When off, you start Ollama yourself.")),
            Ui.Check(T($"Nach {settings.OllamaIdleMinutes} Minuten ohne Dokumente beenden", $"Quit after {settings.OllamaIdleMinutes} minutes without documents"),
                settings.StopOllamaWhenIdle, on => { settings.StopOllamaWhenIdle = on; settings.Save(); },
                T("Gibt Grafikkarte und Arbeitsspeicher wieder komplett frei. Gilt nur, wenn das Tool Ollama selbst gestartet hat. Ein von dir gestartetes Ollama bleibt an.",
                  "Frees the graphics card and memory completely. Only applies if the tool started Ollama itself. An Ollama you started stays on.")),
            Ui.Para(T("Mit der Maus auf einen Schalter zeigen für eine Erklärung. Alle Schalter gibt es auch im Menü des Symbols.",
                      "Point at a switch for an explanation. All switches are also in the icon's menu."), muted: true)));
        cards.Children.Add(AppCard());
        cards.Children.Add(Ui.Card(null, Ui.Fold(T("Über und Lizenzen", "About and licenses"), false, "titel", AboutParts())));

        var root = new Panel();
        root.Children.Add(new GlassBackground());
        root.Children.Add(new ScrollViewer { Content = cards });
        Content = root;

        updates.Changed += ShowUpdateState;
        store.ContactChanged += PostContact;
        Closed += (_, _) =>
        {
            updates.Changed -= ShowUpdateState;
            store.ContactChanged -= PostContact;
        };
        ShowUpdateState();
        ShowContact();
        Opened += async (_, _) =>
        {
            await RefreshServerAsync();
            await LoadModelsAsync(startServer: false);
        };
    }

    Control HeaderCard()
    {
        Version v = UpdateChecker.Current;
        string version = $"Version {v.Major}.{v.Minor}.{v.Build}";
        try
        {
            DateTime built = File.GetLastWriteTime(Environment.ProcessPath!);
            version += T($" · Stand {built:dd.MM.yyyy}", $" · Built {built:yyyy-MM-dd}");
        }
        catch (IOException) { }

        var left = new StackPanel { Spacing = 4 };
        left.Children.Add(Ui.Para(Program.Name, bold: true, size: 24));
        left.Children.Add(Ui.Para(version, muted: true));
        left.Children.Add(Ui.Para(T("von Xviews", "by Xviews"), muted: true));
        left.Children.Add(Ui.Para(T("Lokale KI-Texterkennung für Paperless-ngx: Das Tool gibt sich als Azure Document Intelligence aus " +
                                    "und liest deine Scans mit einem KI-Modell deiner Wahl, komplett auf deinem Rechner.",
                                    "Local AI text recognition for Paperless-ngx: the tool poses as Azure Document Intelligence " +
                                    "and reads your scans with an AI model of your choice, entirely on your own computer.")));
        left.Children.Add(Ui.Row(Ui.Button(T("Projektseite auf GitHub", "Project page on GitHub"), () => Ui.OpenUrl(ProjectUrl))));
        var logo = new Image { Source = App.Image("xviews.png"), Width = 110, Height = 165, Margin = new Thickness(16, 0, 0, 0), VerticalAlignment = VerticalAlignment.Top };
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        grid.Children.Add(left);
        Grid.SetColumn(logo, 1);
        grid.Children.Add(logo);
        return Ui.Card(null, grid);
    }

    Control SetupCard()
    {
        string url = $"http://{LocalAddress()}:{settings.Port}";
        const string envLine = "PAPERLESS_REMOTE_OCR_ALLOW_INTERNAL_ENDPOINTS=true";
        modelBox.SelectionChanged += async (_, _) =>
        {
            if (loadingModels || modelBox.SelectedItem is not string model || model == settings.Model) return;
            settings.Model = model;
            settings.Save();
            Log.Write(T($"Modell gewählt: {model}", $"Model selected: {model}"));
            await RefreshServerAsync();
        };

        var serverBox = new ComboBox { Width = 360 };
        foreach (var s in AiClient.Servers) serverBox.Items.Add(s.Name);
        serverBox.SelectedIndex = Math.Max(0, Array.FindIndex(AiClient.Servers, s => s.Id == settings.Server));
        var urlBox = new TextBox { Text = settings.ServerUrl, MinWidth = 260 };
        async Task ApplyServerAsync()
        {
            string address = (urlBox.Text ?? "").Trim().TrimEnd('/');
            string server = AiClient.Servers[serverBox.SelectedIndex].Id;
            if (server == settings.Server && address == settings.ServerUrl) return;
            if (server != settings.Server) settings.Model = "";
            settings.Server = server;
            settings.ServerUrl = address;
            settings.Save();
            Log.Write(T($"KI-Server: {server} {address}", $"AI server: {server} {address}"));
            await RefreshServerAsync();
            await LoadModelsAsync(startServer: false);
        }
        serverBox.SelectionChanged += async (_, _) =>
        {
            // Beim Wechsel die übliche Adresse des neuen Servers vorschlagen, außer der Nutzer hat eine eigene eingetragen
            if (AiClient.Servers.Any(s => s.DefaultUrl == (urlBox.Text ?? "").Trim()))
                urlBox.Text = AiClient.Servers[serverBox.SelectedIndex].DefaultUrl;
            await ApplyServerAsync();
        };
        urlBox.LostFocus += async (_, _) => await ApplyServerAsync();

        string firewall = OperatingSystem.IsMacOS()
            ? T("Fragt macOS, ob eingehende Verbindungen erlaubt sind, mit „Erlauben“ bestätigen.", "If macOS asks whether to accept incoming connections, click \"Allow\".")
            : T("Fragt die Windows-Firewall nach, den Zugriff im privaten Netzwerk erlauben.", "If the Windows firewall asks, allow access on the private network.");
        // Offen, bis Paperless einmal ein Dokument erfolgreich geschickt hat, und bei falschem Schlüssel
        bool openGuide = !settings.PaperlessConnected || store.LastContact?.Result == ContactResult.WrongKey;
        return Ui.Card(T("Einrichtung", "Setup"),
            serverStatus,
            paperlessStatus,
            Ui.Label(T("KI-Server", "AI server")),
            Ui.Row(serverBox, urlBox),
            Ui.Label(T("KI-Modell", "AI model")),
            Ui.Row(modelBox, Ui.Button(T("Modelle suchen", "Find models"), () => LoadModelsAsync(startServer: true))),
            Ui.Para(T("Ein Modell mit Bilderkennung (Vision) auswählen. Das Tool bringt keins mit.",
                      "Pick a model with image recognition (vision). The tool does not include one."), muted: true),
            new Border { Height = 4 },
            Ui.Fold(T("Paperless einrichten", "Set up Paperless"), openGuide, "feld",
            Ui.Para(T("1. Diese Zeile bei Paperless als Umgebungsvariable eintragen (Docker: unter environment, sonst in der paperless.conf) und Paperless neu starten:",
                      "1. Add this line to Paperless as an environment variable (Docker: under environment, otherwise in paperless.conf) and restart Paperless:")),
            Ui.CopyRow(envLine),
            Ui.Para(T("2. In Paperless unter Einstellungen → Konfiguration → Remote OCR eintragen: Engine „Azure AI“, dazu diese beiden Werte:",
                      "2. In Paperless under Settings → Configuration → Remote OCR, choose the engine \"Azure AI\" and enter these two values:")),
            Ui.Label(T("Endpunkt", "Endpoint")), Ui.CopyRow(url),
            Ui.Label(T("API-Schlüssel", "API key")), Ui.CopyRow(settings.ApiKey),
            Ui.Para(T("3. Modus „Immer“ schickt jedes neue Dokument hierher, „Nur Workflow“ nur Dokumente aus einem Workflow mit der Aktion „Remote OCR“. " +
                      "Das Tool muss laufen, solange Paperless Dokumente verarbeitet. ",
                      "3. The mode \"Always\" sends every new document here, \"Workflow only\" only documents from a workflow with the \"Remote OCR\" action. " +
                      "The tool has to run while Paperless is processing documents. ") + firewall, muted: true)));
    }

    static TextBlock Section(string text)
    {
        var label = Ui.Label(text);
        label.Margin = new Thickness(0, 10, 0, 0);
        return label;
    }

    /// <summary>Updates, Installation und Datenordner in einer Karte.</summary>
    Control AppCard()
    {
        var parts = new List<Control>
        {
            updateStatus,
            Ui.Row(checkButton, installButton, pageButton),
            Ui.Para(T("GitHub wird höchstens einmal am Tag gefragt. Installiert wird nur nach Rückfrage, die Prüfsumme wird geprüft.",
                      "GitHub is asked at most once a day. Updates are only installed after asking, and the checksum is verified."), muted: true),
            Section("Installation"),
        };
        if (Installation.RunningInstalled)
            parts.AddRange([
                Ui.Para(Installation.Folder),
                Ui.Para(Installation.InstalledHint, muted: true),
                Ui.Row(Ui.Button(T("Deinstallieren …", "Uninstall …"), () => Installation.StartUninstaller()))]);
        else
            parts.AddRange([
                Ui.Para(T("Läuft ohne Installation direkt aus dieser Datei.", "Runs without installation, straight from this file.")),
                Ui.Para(Installation.NotInstalledHint, muted: true),
                Ui.Row(Ui.Button(T("Jetzt installieren …", "Install now …"), () => Installation.StartInstaller(), accent: true))]);
        parts.AddRange([
            Section(T("Daten", "Data")),
            Ui.Para(Settings.Folder),
            Ui.Para(T("Einstellungen mit dem API-Schlüssel und Protokolle. Dokumente werden nicht gespeichert, sie liegen nur während der Texterkennung im Arbeitsspeicher.",
                      "Settings with the API key, and logs. Documents are not stored, they only live in memory during text recognition."), muted: true),
            Ui.Row(Ui.Button(T("Ordner öffnen", "Open folder"), () => { Directory.CreateDirectory(Settings.Folder); Ui.OpenUrl(Settings.Folder); }),
                   Ui.Button(T("Protokoll öffnen", "Open log"), OpenLog))]);
        return Ui.Card(T("Programm", "App"), [.. parts]);
    }

    /// <summary>Hinweis, Lizenz und Komponenten; zugeklappt, weil man es beim Einrichten nicht braucht.</summary>
    Control[] AboutParts()
    {
        var rows = new List<Control>
        {
            Ui.Para(T("Privates Projekt ohne Verbindung zu Microsoft, Apple, Ollama, LM Studio oder dem Paperless-ngx-Projekt. " +
                      "„Microsoft“ und „Azure“ sind Marken der Microsoft Corporation, „Mac“ und „macOS“ Marken von Apple Inc. " +
                      "Das Tool bildet nur die Schnittstelle von Azure AI Document Intelligence nach, damit Paperless-ngx es ansprechen kann. " +
                      "Nutzung auf eigene Verantwortung.",
                      "Private project, not affiliated with Microsoft, Apple, Ollama, LM Studio or the Paperless-ngx project. " +
                      "\"Microsoft\" and \"Azure\" are trademarks of Microsoft Corporation, \"Mac\" and \"macOS\" are trademarks of Apple Inc. " +
                      "The tool only imitates the interface of Azure AI Document Intelligence so that Paperless-ngx can talk to it. " +
                      "Use at your own risk.")),
            Section(T("Lizenz", "License")),
            Ui.Para(T("© 2026 Xviews · MIT-Lizenz", "© 2026 Xviews · MIT license"), bold: true),
            Ui.Para(Terms),
            Ui.Row(Ui.Button(T("Quellcode auf GitHub", "Source code on GitHub"), () => Ui.OpenUrl(ProjectUrl))),
            Section(T("Enthaltene Komponenten", "Included components")),
        };
        foreach (CreditLine c in Credits)
        {
            var link = new Button { Content = "↗", Classes = { "leise" }, VerticalAlignment = VerticalAlignment.Top };
            ToolTip.SetTip(link, c.Url);
            link.Click += (_, _) => Ui.OpenUrl(c.Url);
            var texts = new StackPanel { Spacing = 1 };
            texts.Children.Add(Ui.Para(c.Name, bold: true));
            texts.Children.Add(Ui.Para(c.Detail, muted: true));
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 0, 0, 4) };
            grid.Children.Add(texts);
            Grid.SetColumn(link, 1);
            grid.Children.Add(link);
            rows.Add(grid);
        }
        rows.Add(Ui.Row(Ui.Button(T("Lizenzen anzeigen", "Show licenses"), ShowLicenses)));
        return [.. rows];
    }

    void ShowUpdateState()
    {
        updateStatus.Text = updates.Status;
        bool available = updates.Available is not null;
        installButton.IsVisible = pageButton.IsVisible = available;
        checkButton.IsEnabled = installButton.IsEnabled = !updates.Busy;
        if (updates.Available is { } u) installButton.Content = T($"Version {u.VersionText} installieren", $"Install version {u.VersionText}");
    }

    void PostContact() => Dispatcher.UIThread.Post(ShowContact);

    /// <summary>Paperless fragt nur an, wenn es ein Dokument schickt: zeigt die letzte Anfrage seit dem Start.</summary>
    void ShowContact()
    {
        PaperlessContact? c = store.LastContact;
        bool? ok = null;
        if (c is null)
            paperlessStatus.Text = T("Paperless hat seit dem Start noch nichts geschickt. Zum Testen ein Dokument in Paperless hochladen.",
                                     "Paperless has not sent anything since the tool started. Upload a document in Paperless to test.");
        else
        {
            string when = c.Time.Date == DateTime.Today ? T($"um {c.Time:HH:mm}", $"at {c.Time:HH:mm}") : T($"am {c.Time:dd.MM. HH:mm}", $"on {c.Time:MMM d, HH:mm}");
            string who = $"Paperless ({c.From})";
            string pages = c.Pages == 1 ? T("1 Seite", "1 page") : T($"{c.Pages} Seiten", $"{c.Pages} pages");
            (ok, paperlessStatus.Text) = c.Result switch
            {
                ContactResult.WrongKey => (false, T($"{who} hat sich {when} mit falschem API-Schlüssel gemeldet. Den Schlüssel unten kopieren und in Paperless neu eintragen.",
                                                    $"{who} checked in {when} with a wrong API key. Copy the key below and enter it again in Paperless.")),
                ContactResult.Accepted => (true, T($"{who} hat {when} ein Dokument geschickt, es wird gerade gelesen.",
                                                   $"{who} sent a document {when}, it is being read right now.")),
                ContactResult.Succeeded => (true, T($"{who} ist verbunden: {when} ein Dokument mit {pages} erkannt.",
                                                    $"{who} is connected: recognized a document with {pages} {when}.")),
                _ => (false, T($"{who} ist verbunden, aber die Texterkennung {when} ist fehlgeschlagen: {c.Error}",
                               $"{who} is connected, but text recognition {when} failed: {c.Error}")),
            };
        }
        paperlessStatus.Text = (ok switch { true => "✓ ", false => "✕ ", null => "○ " }) + paperlessStatus.Text;
        paperlessStatus.Classes.Set("gut", ok == true);
        paperlessStatus.Classes.Set("schlecht", ok == false);
        paperlessStatus.Classes.Set("leise", ok is null);
    }

    async Task RefreshServerAsync()
    {
        (bool ok, string text) = await store.Ocr.DescribeAsync();
        serverStatus.Text = (ok ? "✓ " : "✕ ") + text;
        serverStatus.Classes.Set("gut", ok);
        serverStatus.Classes.Set("schlecht", !ok);
    }

    /// <summary>Füllt die Modellauswahl; startet Ollama nur, wenn der Nutzer auf „Modelle suchen“ klickt.</summary>
    async Task LoadModelsAsync(bool startServer)
    {
        loadingModels = true;
        modelBox.Items.Clear();
        if (settings.Model != "") modelBox.Items.Add(settings.Model);
        modelBox.SelectedIndex = modelBox.Items.Count > 0 ? 0 : -1;
        List<string>? models = null;
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(startServer ? 90 : 5));
            if (startServer) await store.Ocr.EnsureServerAsync(cts.Token, manual: true);
            models = await store.Ocr.VisionModelsAsync(cts.Token);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or InvalidOperationException or System.Text.Json.JsonException)
        {
            if (startServer)
            {
                serverStatus.Text = "✕ " + e.Message;
                serverStatus.Classes.Set("gut", false);
                serverStatus.Classes.Set("schlecht", true);
            }
        }
        if (models is not null)
        {
            modelBox.Items.Clear();
            foreach (string m in models) modelBox.Items.Add(m);
            if (settings.Model != "" && !models.Contains(settings.Model)) modelBox.Items.Insert(0, settings.Model);
            modelBox.SelectedItem = settings.Model != "" ? settings.Model : models.FirstOrDefault();
        }
        loadingModels = false;
        if (settings.Model == "" && modelBox.SelectedItem is string first)
        {
            settings.Model = first;
            settings.Save();
        }
        if (models is not null) await RefreshServerAsync();
    }

    void ShowLicenses()
    {
        string text;
        using (Stream? stream = typeof(InfoWindow).Assembly.GetManifestResourceStream("Licenses.txt"))
            text = stream is null ? T("Lizenztexte nicht gefunden.", "License texts not found.") : new StreamReader(stream).ReadToEnd();
        var window = new Window
        {
            Title = T("Lizenzen", "Licenses"),
            Icon = App.Icon,
            Width = 860,
            Height = 680,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new TextBox
            {
                Text = text.Replace("\r\n", "\n"),
                IsReadOnly = true,
                AcceptsReturn = true,
                TextWrapping = TextWrapping.NoWrap,
                FontFamily = new FontFamily("Cascadia Mono, Consolas, Menlo, monospace"),
                FontSize = 12.5,
                BorderThickness = new Thickness(0),
            },
        };
        window.Show(this);
    }

    public static void OpenLog()
    {
        Directory.CreateDirectory(Log.Dir);
        if (!File.Exists(Log.CurrentFile)) Log.Write(T("Protokoll geöffnet", "Log opened"));
        Ui.OpenUrl(Log.CurrentFile);
    }

    /// <summary>IPv4-Adresse im Heimnetz (Netzwerkkarte mit Gateway), unter der Paperless das Tool erreicht.</summary>
    static string LocalAddress()
    {
        foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (ni.OperationalStatus != OperationalStatus.Up || ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
            IPInterfaceProperties ip = ni.GetIPProperties();
            if (!ip.GatewayAddresses.Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork)) continue;
            foreach (UnicastIPAddressInformation a in ip.UnicastAddresses)
                if (a.Address.AddressFamily == AddressFamily.InterNetwork) return a.Address.ToString();
        }
        return T("IP-DIESES-RECHNERS", "IP-OF-THIS-COMPUTER");
    }
}
