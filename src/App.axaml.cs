// SPDX-License-Identifier: MIT
// Copyright (C) 2026 Xviews

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace XviewsPaperlessAiOcr;

public partial class App : Application
{
    /// <summary>Was nach dem Start der Oberfläche passiert; legt Program.Main fest.</summary>
    public static Action<IClassicDesktopStyleApplicationLifetime>? Start { get; set; }

    static IClassicDesktopStyleApplicationLifetime? lifetime;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            lifetime = desktop;
            // Das Tool lebt im Infobereich weiter, auch wenn kein Fenster offen ist
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            Start?.Invoke(desktop);
        }
        base.OnFrameworkInitializationCompleted();
    }

    public static void Quit(int code = 0) => lifetime?.Shutdown(code);

    public static Bitmap Image(string name) => new(AssetLoader.Open(new Uri($"avares://XviewsPaperlessAiOcr/Assets/{name}")));

    public static WindowIcon Icon => new(AssetLoader.Open(new Uri("avares://XviewsPaperlessAiOcr/Assets/app.ico")));
}
