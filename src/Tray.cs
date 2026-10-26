// SPDX-License-Identifier: MIT
// Copyright (C) 2026 Xviews

using Avalonia.Controls;
using Avalonia.Threading;

namespace XviewsPaperlessAiOcr;

/// <summary>Symbol im Infobereich (Mac: Menüleiste) mit Status, Info, Update, Schaltern, Protokoll und Beenden.</summary>
sealed class Tray : IDisposable
{
    readonly TrayIcon icon;
    readonly NativeMenuItem status, lastItem, updateItem, autostartItem, notifyItem, errorItem, startItem, idleItem;
    readonly Settings settings;
    readonly JobStore store;
    readonly Updates updates;
    readonly DispatcherTimer idleTimer = new() { Interval = TimeSpan.FromMinutes(1) };
    InfoWindow? info;
    StatusWindow? statusWindow;
    DateTime statusClosed;
    string? announced;

    public Tray(Settings settings, JobStore store, Updates updates, bool showInfoNow)
    {
        this.settings = settings;
        this.store = store;
        this.updates = updates;

        status = new NativeMenuItem(T("Bereit", "Ready")) { IsEnabled = false };
        // Auf dem Mac öffnet ein Klick aufs Symbol immer das Menü, darum steht der Status dort zusätzlich im Menü
        lastItem = new NativeMenuItem("") { IsEnabled = false, IsVisible = OperatingSystem.IsMacOS() };
        var infoItem = Item(T("Info und Einrichtung", "Info and setup"), ShowInfo);
        updateItem = Item("", () => _ = updates.InstallAsync(OpenInfo()));
        updateItem.IsVisible = false;
        autostartItem = Toggle(Autostart.Label, () => Autostart.Enabled = !Autostart.Enabled);
        notifyItem = Toggle(T("Benachrichtigen, wenn ein Dokument fertig ist", "Notify when a document is done"), () =>
        {
            settings.NotifyWhenDone = !settings.NotifyWhenDone;
            settings.Save();
        });
        errorItem = Toggle(T("Fehler melden", "Report errors"), () =>
        {
            settings.NotifyErrors = !settings.NotifyErrors;
            settings.Save();
        });
        startItem = Toggle(T("Ollama bei Bedarf starten", "Start Ollama when needed"), () =>
        {
            settings.StartOllamaOnDemand = !settings.StartOllamaOnDemand;
            settings.Save();
        });
        idleItem = Toggle(T($"Ollama nach {settings.OllamaIdleMinutes} Min. Leerlauf beenden", $"Quit Ollama after {settings.OllamaIdleMinutes} min idle"), () =>
        {
            settings.StopOllamaWhenIdle = !settings.StopOllamaWhenIdle;
            settings.Save();
        });

        var menu = new NativeMenu();
        menu.Add(status);
        menu.Add(lastItem);
        menu.Add(new NativeMenuItemSeparator());
        menu.Add(infoItem);
        menu.Add(updateItem);
        menu.Add(new NativeMenuItemSeparator());
        menu.Add(autostartItem);
        menu.Add(notifyItem);
        menu.Add(errorItem);
        menu.Add(startItem);
        menu.Add(idleItem);
        menu.Add(new NativeMenuItemSeparator());
        menu.Add(Item(T("Protokoll öffnen", "Open log"), InfoWindow.OpenLog));
        menu.Add(Item(T("Beenden", "Quit"), () => App.Quit()));
        // Opening kommt unter Windows am Symbol im Infobereich nicht an, darum zusätzlich bei jeder Änderung auffrischen
        menu.Opening += (_, _) => RefreshMenu();
        settings.Saved += RefreshMenuLater;
        Autostart.Changed += RefreshMenuLater;
        store.ContactChanged += RefreshMenuLater;
        store.JobSucceeded += (_, _) => RefreshMenuLater();
        store.JobFailed += _ => RefreshMenuLater();
        RefreshMenu();

        icon = new TrayIcon { Icon = App.Icon, ToolTipText = Program.Name, Menu = menu, IsVisible = true };
        icon.Clicked += (_, _) => ToggleStatus();
        TrayIcon.SetIcons(Avalonia.Application.Current!, [icon]);

        // Meldungen kommen aus dem Hintergrund, die Fenster gehören in den UI-Thread
        store.JobFailed += message => Dispatcher.UIThread.Post(() =>
        {
            if (settings.NotifyErrors)
                Notification.Show(T("Texterkennung fehlgeschlagen", "Text recognition failed"), message, ShowInfo, error: true, seconds: 15);
        });
        store.WrongKey += from => Dispatcher.UIThread.Post(() =>
        {
            if (settings.NotifyErrors)
                Notification.Show(T("Falscher API-Schlüssel", "Wrong API key"),
                    T($"Paperless ({from}) hat ein Dokument mit einem falschen API-Schlüssel geschickt. Klicken, um den richtigen Schlüssel zu kopieren.",
                      $"Paperless ({from}) sent a document with a wrong API key. Click to copy the correct key."), ShowInfo, error: true, seconds: 15);
        });
        store.JobSucceeded += (pages, duration) => Dispatcher.UIThread.Post(() =>
        {
            if (settings.NotifyWhenDone)
                Notification.Show(T("Dokument erkannt", "Document recognized"),
                    T($"{pages} {(pages == 1 ? "Seite" : "Seiten")} in {duration.TotalSeconds:0} Sekunden gelesen und an Paperless zurückgegeben.",
                      $"Read {pages} {(pages == 1 ? "page" : "pages")} in {duration.TotalSeconds:0} seconds and returned to Paperless."), ShowInfo);
        });

        updates.Changed += ShowUpdate;
        ShowUpdate();
        if (showInfoNow) ShowInfo();

        DispatcherTimer.RunOnce(async () => await updates.CheckOnStartAsync(), TimeSpan.FromSeconds(3));
        idleTimer.Tick += (_, _) =>
        {
            if (settings.StopOllamaWhenIdle && store.Ocr.StartedByUs && store.Active == 0
                && DateTime.Now - store.LastActivity > TimeSpan.FromMinutes(settings.OllamaIdleMinutes))
                store.Ocr.StopOllama();
        };
        idleTimer.Start();
    }

    static NativeMenuItem Item(string text, Action click)
    {
        var item = new NativeMenuItem(text);
        item.Click += (_, _) => click();
        return item;
    }

    static NativeMenuItem Toggle(string text, Action click)
    {
        var item = new NativeMenuItem(text) { ToggleType = MenuItemToggleType.CheckBox };
        item.Click += (_, _) => click();
        return item;
    }

    void RefreshMenuLater() => Dispatcher.UIThread.Post(RefreshMenu);

    void RefreshMenu()
    {
        autostartItem.IsChecked = Autostart.Enabled;
        notifyItem.IsChecked = settings.NotifyWhenDone;
        errorItem.IsChecked = settings.NotifyErrors;
        startItem.IsChecked = settings.StartOllamaOnDemand;
        idleItem.IsChecked = settings.StopOllamaWhenIdle;
        status.Header = store.Active > 0
            ? T($"Arbeitet: {store.Active} Auftrag/Aufträge", $"Working: {store.Active} job(s)")
            : T("Bereit", "Ready") + $": Port {settings.Port}, {(settings.Model == "" ? T("kein Modell gewählt", "no model selected") : settings.Model)}";
        int n = store.Recognized;
        lastItem.Header = T($"Zuletzt: {StatusWindow.LastText(store)} · seit dem Start {n}", $"Last: {StatusWindow.LastText(store)} · since start {n}");
    }

    /// <summary>
    /// Klick aufs Symbol: kleines Statusfenster auf und zu. Ohne Modell gibt es nichts zu melden,
    /// dann gleich das Info-Fenster zum Einrichten.
    /// </summary>
    void ToggleStatus()
    {
        if (settings.Model == "")
        {
            ShowInfo();
            return;
        }
        if (statusWindow is not null)
        {
            statusWindow.Close();
            return;
        }
        // Der Klick aufs Symbol hat das Fenster eben schon geschlossen (Deactivated): nicht gleich wieder öffnen
        if (DateTime.Now - statusClosed < TimeSpan.FromMilliseconds(400)) return;
        statusWindow = new StatusWindow(settings, store, ShowInfo);
        statusWindow.Closed += (_, _) => { statusWindow = null; statusClosed = DateTime.Now; };
        statusWindow.Show();
        statusWindow.Activate();
    }

    void ShowUpdate()
    {
        updateItem.IsVisible = updates.Available is not null;
        updateItem.IsEnabled = !updates.Busy;
        if (updates.Available is not { } u) return;
        updateItem.Header = T($"Version {u.VersionText} installieren", $"Install version {u.VersionText}");
        if (announced == u.VersionText) return;
        announced = u.VersionText;
        Notification.Show(T($"Version {u.VersionText} ist verfügbar", $"Version {u.VersionText} is available"),
            T("Klicken, um das Info-Fenster zu öffnen und zu aktualisieren.", "Click to open the info window and update."), ShowInfo, seconds: 12);
    }

    /// <summary>Wo das Symbol sitzt, für Texte wie „über das Symbol im Infobereich“.</summary>
    public static string Area => OperatingSystem.IsMacOS() ? T("in der Menüleiste", "in the menu bar") : T("im Infobereich", "in the notification area");

    public void ShowInfo() => OpenInfo();

    InfoWindow OpenInfo()
    {
        if (info is { IsVisible: true })
        {
            if (info.WindowState == WindowState.Minimized) info.WindowState = WindowState.Normal;
            info.Activate();
            return info;
        }
        info = new InfoWindow(settings, updates, store);
        info.Closed += (_, _) => info = null;
        info.Show();
        return info;
    }

    public void Dispose()
    {
        idleTimer.Stop();
        icon.IsVisible = false;
        icon.Dispose();
        info?.Close();
        statusWindow?.Close();
    }
}
