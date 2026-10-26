// SPDX-License-Identifier: MIT
// Copyright (C) 2026 Xviews

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using FluentAvalonia.UI.Controls;

namespace XviewsPaperlessAiOcr;

/// <summary>Bausteine für die Fenster: Karten, Texte, Knöpfe, Kopierzeilen.</summary>
static class Ui
{
    public static Border Card(string? title, params Control[] content)
    {
        var stack = new StackPanel { Spacing = 6 };
        if (title is not null) stack.Children.Add(new TextBlock { Text = title, Classes = { "titel" } });
        stack.Children.AddRange(content);
        return new Border { Classes = { "karte" }, Child = stack };
    }

    public static TextBlock Para(string text, bool muted = false, bool bold = false, double size = 0)
    {
        var t = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap };
        if (muted) t.Classes.Add("leise");
        if (bold) t.FontWeight = FontWeight.SemiBold;
        if (size > 0) t.FontSize = size;
        return t;
    }

    public static TextBlock Label(string text) => new() { Text = text, Classes = { "feld" } };

    public static Button Button(string text, Action click, bool accent = false)
    {
        var b = new Button { Content = text };
        if (accent) b.Classes.Add("accent");
        b.Click += (_, _) => click();
        return b;
    }

    public static Button Button(string text, Func<Task> click, bool accent = false)
    {
        var b = new Button { Content = text };
        if (accent) b.Classes.Add("accent");
        b.Click += async (_, _) => await click();
        return b;
    }

    public static StackPanel Row(params Control[] content)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 4, 0, 0) };
        row.Children.AddRange(content);
        return row;
    }

    /// <summary>Wert zum Kopieren (nur lesen) mit Knopf „Kopieren“.</summary>
    public static Control CopyRow(string value)
    {
        var box = new TextBox { Text = value, Classes = { "wert" } };
        var copy = new Button { Content = T("Kopieren", "Copy"), VerticalAlignment = VerticalAlignment.Stretch };
        copy.Click += async (_, _) =>
        {
            if (TopLevel.GetTopLevel(copy)?.Clipboard is { } clipboard)
            {
                await clipboard.SetTextAsync(value);
                copy.Content = T("Kopiert ✓", "Copied ✓");
            }
        };
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 8 };
        grid.Children.Add(box);
        Grid.SetColumn(copy, 1);
        grid.Children.Add(copy);
        return grid;
    }

    /// <param name="tip">Erklärung beim Darüberfahren mit der Maus.</param>
    public static CheckBox Check(string text, bool value, Action<bool> changed, string? tip = null)
    {
        var box = new CheckBox { Content = text, IsChecked = value, Margin = new Thickness(0, 2, 0, 0) };
        box.IsCheckedChanged += (_, _) => changed(box.IsChecked == true);
        if (tip is not null) ToolTip.SetTip(box, new TextBlock { Text = tip, TextWrapping = TextWrapping.Wrap, MaxWidth = 360 });
        return box;
    }

    /// <summary>Aufklappbarer Abschnitt: Überschrift mit Pfeil, ein Klick darauf zeigt oder verbirgt den Inhalt.</summary>
    /// <param name="titleClass">„titel“ für eine ganze Karte, „feld“ für einen Abschnitt in einer Karte.</param>
    public static Control Fold(string title, bool open, string titleClass, params Control[] content)
    {
        var body = new StackPanel { Spacing = 6, IsVisible = open, Margin = new Thickness(0, 6, 0, 0) };
        body.Children.AddRange(content);
        var arrow = new Avalonia.Controls.Shapes.Path
        {
            Data = Geometry.Parse("M 1,1 L 6,6 L 11,1"),
            StrokeThickness = 1.6,
            Width = 12,
            Height = 7,
            VerticalAlignment = VerticalAlignment.Center,
            RenderTransform = new RotateTransform(open ? 0 : -90),
        };
        arrow.Bind(Avalonia.Controls.Shapes.Shape.StrokeProperty, arrow.GetResourceObservable("TextFillColorSecondaryBrush"));
        var head = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Background = Brushes.Transparent,
            Cursor = new Cursor(StandardCursorType.Hand),
        };
        head.Children.Add(new TextBlock { Text = title, Classes = { titleClass }, VerticalAlignment = VerticalAlignment.Center });
        Grid.SetColumn(arrow, 1);
        head.Children.Add(arrow);
        head.PointerPressed += (_, _) =>
        {
            body.IsVisible = !body.IsVisible;
            arrow.RenderTransform = new RotateTransform(body.IsVisible ? 0 : -90);
        };
        var stack = new StackPanel();
        stack.Children.Add(head);
        stack.Children.Add(body);
        return stack;
    }

    public static void OpenUrl(string url) =>
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true })?.Dispose();
}

/// <summary>
/// Hintergrund eines Fensters: ruhige Fläche (dunkel fast schwarz, hell fast weiß) mit einem sanften
/// grünen Schimmer oben links. Gehört als unterstes Element in ein Fenster mit Classes="glas".
/// </summary>
sealed class GlassBackground : Panel
{
    public GlassBackground()
    {
        IsHitTestVisible = false;
        Apply();
        ActualThemeVariantChanged += (_, _) => Apply();
    }

    void Apply()
    {
        bool dark = ActualThemeVariant == ThemeVariant.Dark;
        Children.Clear();
        Children.Add(new Border { Background = new SolidColorBrush(Color.Parse(dark ? "#0E100F" : "#F3F5F4")) });
        Children.Add(new Border
        {
            Background = new RadialGradientBrush
            {
                Center = new RelativePoint(0.15, -0.1, RelativeUnit.Relative),
                GradientOrigin = new RelativePoint(0.15, -0.1, RelativeUnit.Relative),
                RadiusX = new RelativeScalar(0.9, RelativeUnit.Relative),
                RadiusY = new RelativeScalar(0.75, RelativeUnit.Relative),
                GradientStops =
                {
                    new GradientStop(Color.Parse(dark ? "#332FBF68" : "#262FBF68"), 0),
                    new GradientStop(Color.Parse("#002FBF68"), 1),
                },
            },
        });
    }
}

/// <summary>Rückfragen und Meldungen im Windows-11-Stil, immer über einem Fenster.</summary>
static class Dialogs
{
    static readonly SemaphoreSlim Gate = new(1, 1);

    public static async Task<bool> AskAsync(Window owner, string title, string text, string yes, string? no = null)
        => await ShowAsync(owner, new FAContentDialog
        {
            Title = title,
            Content = Text(text),
            PrimaryButtonText = yes,
            CloseButtonText = no ?? T("Abbrechen", "Cancel"),
            DefaultButton = FAContentDialogButton.Primary,
        }) == FAContentDialogResult.Primary;

    public static Task InfoAsync(Window owner, string title, string text)
        => ShowAsync(owner, new FAContentDialog { Title = title, Content = Text(text), CloseButtonText = "OK" });

    static async Task<FAContentDialogResult> ShowAsync(Window owner, FAContentDialog dialog)
    {
        // Nur ein Dialog zur Zeit, sonst wirft FAContentDialog
        await Gate.WaitAsync();
        try { return await dialog.ShowAsync(owner); }
        finally { Gate.Release(); }
    }

    static Control Text(string text) => new SelectableTextBlock { Text = text, TextWrapping = TextWrapping.Wrap, MaxWidth = 460 };
}

/// <summary>
/// Kleine Meldung unten rechts über der Taskleiste (Avalonia hat keine Sprechblase am Infobereich-Symbol).
/// Verschwindet nach ein paar Sekunden, ein Klick öffnet das Info-Fenster.
/// </summary>
sealed class Notification : Window
{
    static readonly List<Notification> Open = [];

    Notification(string title, string text, bool error)
    {
        Title = Program.Name;
        Width = 380;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        ShowInTaskbar = false;
        Topmost = true;
        ShowActivated = false;
        WindowDecorations = WindowDecorations.None;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Background = Brushes.Transparent;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        Classes.Add("glas");

        var close = new Button { Content = "✕", Classes = { "leise" }, VerticalAlignment = VerticalAlignment.Top, Padding = new Thickness(8, 4) };
        close.Click += (_, e) => { e.Handled = true; Close(); };
        var texts = new StackPanel { Spacing = 3 };
        var head = Ui.Para(title, bold: true);
        if (error) head.Classes.Add("schlecht");
        texts.Children.Add(head);
        texts.Children.Add(Ui.Para(text, muted: true));
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), Margin = new Thickness(14, 12, 6, 14) };
        grid.Children.Add(new Image { Source = App.Image("logo.png"), Width = 28, Height = 28, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 2, 12, 0) });
        Grid.SetColumn(texts, 1);
        grid.Children.Add(texts);
        Grid.SetColumn(close, 2);
        grid.Children.Add(close);
        var panel = new Panel();
        panel.Children.Add(new GlassBackground());
        panel.Children.Add(grid);
        Content = new Border
        {
            CornerRadius = new CornerRadius(14),
            ClipToBounds = true,
            BorderThickness = new Thickness(1),
            BorderBrush = (IBrush?)Application.Current?.FindResource("CardStrokeColorDefaultBrush"),
            Cursor = new Cursor(StandardCursorType.Hand),
            Child = panel,
        };
    }

    public static void Show(string title, string text, Action? click = null, bool error = false, int seconds = 8)
    {
        var n = new Notification(title, text, error);
        n.PointerPressed += (_, _) => { n.Close(); click?.Invoke(); };
        n.Closed += (_, _) => { Open.Remove(n); Arrange(); };
        n.Opened += (_, _) => Arrange();
        // Beim Öffnen hat das Fenster noch nicht seine endgültige Höhe; danach erneut stapeln
        n.SizeChanged += (_, _) => Arrange();
        Open.Add(n);
        n.Show();
        DispatcherTimer.RunOnce(() => { if (n.IsVisible) n.Close(); }, TimeSpan.FromSeconds(seconds));
    }

    /// <summary>Übereinander unten rechts auf dem Hauptbildschirm stapeln.</summary>
    static void Arrange()
    {
        var screen = Open.FirstOrDefault()?.Screens.Primary;
        if (screen is null) return;
        var area = screen.WorkingArea;
        double s = screen.Scaling;
        int y = area.Bottom - (int)(12 * s);
        foreach (var n in Open)
        {
            int width = (int)(n.Width * s), height = (int)(Math.Max(n.Bounds.Height, 70) * s);
            y -= height;
            n.Position = new PixelPoint(area.Right - width - (int)(12 * s), y);
            y -= (int)(8 * s);
        }
    }
}
