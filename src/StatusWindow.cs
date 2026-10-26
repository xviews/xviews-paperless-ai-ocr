// SPDX-License-Identifier: MIT
// Copyright (C) 2026 Xviews

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace XviewsPaperlessAiOcr;

/// <summary>
/// Kleines Statusfenster beim Klick aufs Symbol: läuft alles, was wird gerade gelesen, was kam zuletzt.
/// Erscheint in der Mitte des Bildschirms und schließt sich, sobald man woanders hinklickt.
/// </summary>
sealed class StatusWindow : Window
{
    readonly Settings settings;
    readonly JobStore store;
    readonly Action openSettings;

    readonly Ellipse dot = new() { Width = 10, Height = 10, VerticalAlignment = VerticalAlignment.Center };
    readonly TextBlock state = Ui.Para("");
    readonly TextBlock detail = Ui.Para("", muted: true);
    // Ohne Zwischenstand je Seite: läuft als Band, die Seitenzahl steht im Text
    readonly ProgressBar progress = new() { IsIndeterminate = true, IsVisible = false };
    readonly TextBlock server = Value(), model = Value(), last = Value(), count = Value();
    readonly Button copyKey;
    readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(1) };
    IDisposable? dotBrush;
    bool serverUp, serverOk = true;
    string serverText = "";
    int ticks;
    DateTime opened;

    public StatusWindow(Settings settings, JobStore store, Action openSettings)
    {
        this.settings = settings;
        this.store = store;
        this.openSettings = openSettings;

        Title = Program.Name;
        Width = 360;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        ShowInTaskbar = false;
        Topmost = true;
        WindowDecorations = WindowDecorations.None;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Background = Brushes.Transparent;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        Classes.Add("glas");

        copyKey = Ui.Button(T("Schlüssel kopieren", "Copy key"), async () =>
        {
            if (Clipboard is { } clipboard)
            {
                await clipboard.SetTextAsync(settings.ApiKey);
                copyKey!.Content = T("Kopiert ✓", "Copied ✓");
            }
        });

        var name = Ui.Para(Program.Name.Replace("Xviews ", ""), bold: true);
        var head = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 12 };
        var close = new Button { Content = "✕", Classes = { "leise" }, VerticalAlignment = VerticalAlignment.Top, Padding = new Thickness(8, 4), Margin = new Thickness(0, -4, -6, 0) };
        ToolTip.SetTip(close, T("Schließen", "Close"));
        close.Click += (_, _) => Close();
        Grid.SetColumn(close, 2);
        head.Children.Add(close);
        head.Children.Add(new Image { Source = App.Image("logo.png"), Width = 32, Height = 32, VerticalAlignment = VerticalAlignment.Top });
        var titles = new StackPanel { Spacing = 1 };
        titles.Children.Add(name);
        var stateRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7 };
        stateRow.Children.Add(dot);
        stateRow.Children.Add(state);
        titles.Children.Add(stateRow);
        Grid.SetColumn(titles, 1);
        head.Children.Add(titles);

        var rows = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 16, RowSpacing = 5 };
        AddRow(rows, 0, T("KI-Server", "AI server"), server);
        AddRow(rows, 1, T("Modell", "Model"), model);
        AddRow(rows, 2, T("Zuletzt", "Last"), last);
        AddRow(rows, 3, T("Seit dem Start", "Since start"), count);

        // Umbrechen statt abschneiden, wenn „Schlüssel kopieren“ dazukommt
        var buttons = new WrapPanel { ItemSpacing = 8, LineSpacing = 8, Margin = new Thickness(0, 6, 0, 0) };
        buttons.Children.Add(copyKey);
        buttons.Children.Add(Ui.Button(T("Einstellungen", "Settings"), () => { Close(); openSettings(); }));
        buttons.Children.Add(Ui.Button(T("Protokoll", "Log"), () => { Close(); InfoWindow.OpenLog(); }));
        var line = new Border { Height = 1 };
        line.Bind(Border.BackgroundProperty, line.GetResourceObservable("CardStrokeColorDefaultBrush"));

        var stack = new StackPanel { Spacing = 10, Margin = new Thickness(16, 14, 16, 16) };
        stack.Children.Add(head);
        stack.Children.Add(progress);
        stack.Children.Add(detail);
        stack.Children.Add(line);
        stack.Children.Add(rows);
        stack.Children.Add(buttons);

        var panel = new Panel();
        panel.Children.Add(new GlassBackground());
        panel.Children.Add(stack);
        Content = new Border
        {
            CornerRadius = new CornerRadius(14),
            ClipToBounds = true,
            BorderThickness = new Thickness(1),
            BorderBrush = (IBrush?)Application.Current?.FindResource("CardStrokeColorDefaultBrush"),
            Child = panel,
        };

        Update();
        Opened += (_, _) =>
        {
            opened = DateTime.Now;
            PlaceCentered();
            _ = CheckServerAsync();
            // Sitzt das Symbol bei den ausgeblendeten Symbolen, holt sich deren Fenster beim Zuklappen den Fokus zurück
            DispatcherTimer.RunOnce(Activate, TimeSpan.FromMilliseconds(300));
        };
        // Klick daneben oder Escape schließt das Fenster, nur nicht im ersten Moment (siehe oben)
        Deactivated += (_, _) => { if (DateTime.Now - opened > TimeSpan.FromMilliseconds(600)) Close(); };
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
        SizeChanged += (_, _) => PlaceCentered();
        timer.Tick += (_, _) =>
        {
            if (++ticks % 5 == 0) _ = CheckServerAsync();
            Update();
        };
        timer.Start();
        Closed += (_, _) => { timer.Stop(); dotBrush?.Dispose(); };
    }

    static TextBlock Value() => new() { TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Right };

    static void AddRow(Grid grid, int row, string label, TextBlock value)
    {
        grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        var l = Ui.Para(label, muted: true);
        Grid.SetRow(l, row);
        Grid.SetRow(value, row);
        Grid.SetColumn(value, 1);
        grid.Children.Add(l);
        grid.Children.Add(value);
    }

    async Task CheckServerAsync()
    {
        serverUp = await store.Ocr.IsRunningAsync();
        (serverOk, serverText) = await store.Ocr.DescribeAsync();
        Update();
    }

    void Update()
    {
        OcrJob? job = store.Running;
        PaperlessContact? c = store.LastContact;
        (string text, string? info, string brush) = (job, c?.Result) switch
        {
            ({ PageCount: 0 }, _) => (T("Bereitet ein Dokument vor …", "Preparing a document …"), null, "GutText"),
            ({ } j, _) => (T($"Liest Seite {Math.Min(j.Pages.Count + 1, j.PageCount)} von {j.PageCount}", $"Reading page {Math.Min(j.Pages.Count + 1, j.PageCount)} of {j.PageCount}"), null, "GutText"),
            (null, ContactResult.WrongKey) => (T("Falscher API-Schlüssel", "Wrong API key"),
                T($"Paperless ({c!.From}) hat sich {When(c.Time)} mit falschem Schlüssel gemeldet. Den Schlüssel kopieren und in Paperless neu eintragen.",
                  $"Paperless ({c!.From}) checked in {When(c.Time)} with a wrong key. Copy the key and enter it again in Paperless."), "RotText"),
            (null, ContactResult.Failed) => (T("Texterkennung fehlgeschlagen", "Text recognition failed"), c!.Error, "RotText"),
            _ when !serverOk => (T("Nicht bereit", "Not ready"), serverText, "RotText"),
            _ => (T("Bereit", "Ready"), null, "GutText"),
        };

        state.Text = text;
        state.Classes.Set("gut", brush == "GutText");
        state.Classes.Set("schlecht", brush == "RotText");
        dotBrush?.Dispose();
        dotBrush = dot.Bind(Shape.FillProperty, this.GetResourceObservable(brush));
        detail.Text = info ?? "";
        detail.IsVisible = info is not null;
        copyKey.IsVisible = c?.Result == ContactResult.WrongKey && job is null;

        progress.IsVisible = job is not null;

        server.Text = serverUp ? T($"{store.Ocr.ShortName} läuft", $"{store.Ocr.ShortName} is running")
            : store.Ocr.StartsOnDemand ? T("Ollama startet bei Bedarf", "Ollama starts when needed")
            : T($"{store.Ocr.ShortName} läuft nicht", $"{store.Ocr.ShortName} is not running");
        model.Text = settings.Model == "" ? T("keins gewählt", "none selected") : settings.Model;
        last.Text = LastText(store);
        int n = store.Recognized;
        count.Text = n == 1 ? T("1 Dokument", "1 document") : T($"{n} Dokumente", $"{n} documents");
    }

    /// <summary>Kurzfassung der letzten Anfrage von Paperless, auch für das Menü auf dem Mac.</summary>
    public static string LastText(JobStore store) => store.LastContact switch
    {
        null => T("noch nichts", "nothing yet"),
        { Result: ContactResult.Succeeded } c => T($"{When(c.Time)}, {Pages(c.Pages)} in {c.Duration.TotalSeconds:0} s",
                                                   $"{When(c.Time)}, {Pages(c.Pages)} in {c.Duration.TotalSeconds:0} s"),
        { Result: ContactResult.Accepted } c => T($"{When(c.Time)}, wird gelesen", $"{When(c.Time)}, being read"),
        { Result: ContactResult.WrongKey } c => T($"{When(c.Time)}, abgewiesen", $"{When(c.Time)}, rejected"),
        { } c => T($"{When(c.Time)}, fehlgeschlagen", $"{When(c.Time)}, failed"),
    };

    static string Pages(int n) => n == 1 ? T("1 Seite", "1 page") : T($"{n} Seiten", $"{n} pages");

    static string When(DateTime t) => t.Date == DateTime.Today ? T($"um {t:HH:mm}", $"at {t:HH:mm}") : T($"am {t:dd.MM. HH:mm}", $"on {t:MMM d, HH:mm}");

    /// <summary>In der Mitte des Hauptbildschirms, auch wenn die Höhe sich später ändert.</summary>
    void PlaceCentered()
    {
        if (Screens.Primary is not { } screen) return;
        var area = screen.WorkingArea;
        double s = screen.Scaling;
        int width = (int)(Width * s), height = (int)(Math.Max(Bounds.Height, 100) * s);
        Position = new PixelPoint(area.X + (area.Width - width) / 2, area.Y + (area.Height - height) / 2);
    }
}
