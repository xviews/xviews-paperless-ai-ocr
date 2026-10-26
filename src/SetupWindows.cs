// SPDX-License-Identifier: MIT
// Copyright (C) 2026 Xviews

using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace XviewsPaperlessAiOcr;

enum InstallChoice { Install, Portable, Cancel }

/// <summary>Kleines Fenster im Stil des ClaDuo Profil-Managers: Logo, Titel, Text, ein großer Knopf.</summary>
abstract class SetupWindow : Window
{
    protected readonly TextBlock Heading = new() { FontSize = 22, FontWeight = FontWeight.Bold, HorizontalAlignment = HorizontalAlignment.Center, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap };
    protected readonly TextBlock Body = new() { Classes = { "leise" }, HorizontalAlignment = HorizontalAlignment.Center, TextAlignment = TextAlignment.Center };
    protected readonly StackPanel Stack = new() { Margin = new Thickness(32, 36, 32, 24), Spacing = 18 };

    protected SetupWindow(string logo)
    {
        Title = Program.Name;
        Icon = App.Icon;
        Width = 500;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Classes.Add("glas");

        Stack.Children.Add(new Image { Source = App.Image(logo), Width = 72, Height = 72, HorizontalAlignment = HorizontalAlignment.Center });
        var texts = new StackPanel { Spacing = 6 };
        texts.Children.Add(Heading);
        texts.Children.Add(Body);
        Stack.Children.Add(texts);
        var root = new Panel();
        root.Children.Add(new GlassBackground());
        root.Children.Add(Stack);
        Content = root;
    }

    protected static Button BigButton(string text) => new()
    {
        Content = text,
        Classes = { "accent", "rund" },
        HorizontalAlignment = HorizontalAlignment.Stretch,
        HorizontalContentAlignment = HorizontalAlignment.Center,
        Padding = new Thickness(14, 10),
        FontSize = 14,
        IsDefault = true,
    };

    protected static Button SmallButton(string text, HorizontalAlignment align) =>
        new() { Content = text, Classes = { "leise" }, HorizontalAlignment = align };
}

/// <summary>Beim Start aus einem anderen Ordner: installieren, aktualisieren oder ohne Installation starten.</summary>
sealed class InstallWindow : SetupWindow
{
    readonly TaskCompletionSource<InstallChoice> choice = new();

    public Task<InstallChoice> Choice => choice.Task;

    /// <param name="installed">Version der vorhandenen Installation, null wenn noch keine da ist.</param>
    public InstallWindow(Version? installed) : base("logo.png")
    {
        Version own = UpdateChecker.Current;
        string ownText = $"{own.Major}.{own.Minor}.{own.Build}";
        Button install;
        if (installed is null)
        {
            Heading.Text = T($"{Program.Name} installieren?", $"Install {Program.Name}?");
            Body.Text = Installation.InstallBody;
            install = BigButton(T("Installieren", "Install"));
        }
        else
        {
            Heading.Text = T($"{Program.Name} aktualisieren?", $"Update {Program.Name}?");
            Body.Text = (installed == own
                            ? T($"Installiert ist ein anderer Stand von Version {ownText}. ", $"A different build of version {ownText} is installed. ")
                            : T($"Installiert ist Version {installed}, diese Datei ist Version {ownText}. ", $"Version {installed} is installed, this file is version {ownText}. ")) +
                        T("Einstellungen und API-Schlüssel bleiben, wie sie sind.", "Settings and API key stay as they are.");
            install = BigButton(T("Aktualisieren", "Update"));
        }

        var target = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        target.Children.Add(new TextBlock { Text = T("Ziel", "Target"), Classes = { "leise" }, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) });
        var folder = new TextBlock { Text = Installation.Folder, FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        ToolTip.SetTip(folder, Installation.Folder);
        Grid.SetColumn(folder, 1);
        target.Children.Add(folder);
        Stack.Children.Add(new Border { Classes = { "karte" }, Padding = new Thickness(14, 10), CornerRadius = new CornerRadius(12), Child = target });

        Stack.Children.Add(install);
        var portable = SmallButton(T("Ohne Installation starten", "Start without installing"), HorizontalAlignment.Left);
        var cancel = SmallButton(T("Abbrechen", "Cancel"), HorizontalAlignment.Right);
        cancel.IsCancel = true;
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), Margin = new Thickness(0, -6, 0, 0) };
        row.Children.Add(portable);
        Grid.SetColumn(cancel, 1);
        row.Children.Add(cancel);
        Stack.Children.Add(row);

        install.Click += (_, _) => Done(InstallChoice.Install);
        portable.Click += (_, _) => Done(InstallChoice.Portable);
        cancel.Click += (_, _) => Done(InstallChoice.Cancel);
        Closed += (_, _) => choice.TrySetResult(InstallChoice.Cancel);
    }

    void Done(InstallChoice c)
    {
        choice.TrySetResult(c);
        Close();
    }
}

/// <summary>
/// Deinstallieren (Startmenü „… deinstallieren“, „Installierte Apps“ oder --deinstallieren):
/// zeigt, was entfernt wird und was bleibt, und danach das Ergebnis.
/// </summary>
sealed class UninstallWindow : SetupWindow
{
    bool finished;

    /// <param name="uninstall">Beendet ein laufendes Tool und deinstalliert; liefert eine Fehlermeldung oder null.</param>
    public UninstallWindow(Func<bool, Task<string?>> uninstall) : base("logo.png")
    {
        Heading.Text = T($"{Program.Name} deinstallieren?", $"Uninstall {Program.Name}?");
        Body.Text = T("Paperless nutzt danach wieder seine eigene Texterkennung, sobald du dort Remote OCR ausschaltest.",
                      "Paperless goes back to its own text recognition once you switch off Remote OCR there.");

        var deleteData = new CheckBox { Content = T("Einstellungen und Protokolle auch löschen", "Also delete settings and logs"), Margin = new Thickness(0, 6, 0, 0) };
        var list = new StackPanel { Spacing = 3 };
        list.Children.Add(Section(T("Wird entfernt", "Will be removed"), "schlecht"));
        foreach (string s in Installation.RemovedItems)
            list.Children.Add(new TextBlock { Text = s, Classes = { "leise" } });
        list.Children.Add(Section(T("Bleibt erhalten, außer du hakst es an", "Stays unless you tick it"), "gut"));
        list.Children.Add(new TextBlock { Text = T($"Einstellungen mit API-Schlüssel und Protokolle in {Settings.Folder}", $"Settings with API key and logs in {Settings.Folder}"), Classes = { "leise" } });
        list.Children.Add(deleteData);
        var card = new Border { Classes = { "karte" }, Child = list };
        Stack.Children.Add(card);

        var go = BigButton(T("Deinstallieren", "Uninstall"));
        go.Classes.Remove("accent");
        go.Background = new SolidColorBrush(Color.Parse("#C0392B"));
        go.Foreground = Brushes.White;
        var cancel = SmallButton(T("Abbrechen", "Cancel"), HorizontalAlignment.Center);
        cancel.IsCancel = true;
        Stack.Children.Add(go);
        Stack.Children.Add(cancel);

        cancel.Click += (_, _) => Close();
        go.Click += async (_, _) =>
        {
            if (finished) { Close(); return; }
            go.IsEnabled = cancel.IsEnabled = false;
            go.Content = T("Wird entfernt …", "Removing …");
            bool data = deleteData.IsChecked == true;
            string? error = await uninstall(data);

            finished = true;
            go.IsEnabled = true;
            cancel.IsVisible = card.IsVisible = false;
            go.ClearValue(BackgroundProperty);
            go.ClearValue(ForegroundProperty);
            go.Classes.Add("accent");
            go.Content = T("Schließen", "Close");
            if (error is null)
            {
                Heading.Text = T("Deinstalliert", "Uninstalled");
                Body.Text = data
                    ? T($"{Program.Name} wurde komplett entfernt.", $"{Program.Name} has been removed completely.")
                    : T($"{Program.Name} wurde entfernt. Einstellungen und Protokolle liegen weiter unter {Settings.Folder}.",
                        $"{Program.Name} has been removed. Settings and logs are still in {Settings.Folder}.");
            }
            else
            {
                Heading.Text = T("Nicht deinstalliert", "Not uninstalled");
                Body.Text = error;
            }
        };
    }

    static TextBlock Section(string text, string cls) =>
        new() { Text = text, FontWeight = FontWeight.SemiBold, FontSize = 13, Classes = { cls }, Margin = new Thickness(0, 6, 0, 0) };
}
