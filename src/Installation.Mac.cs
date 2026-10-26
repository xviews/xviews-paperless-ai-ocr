// SPDX-License-Identifier: MIT
// Copyright (C) 2026 Xviews

using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace XviewsPaperlessAiOcr;

/// <summary>
/// Mac-Installation: die App liegt fest unter /Programme (ohne Schreibrecht dort unter ~/Programme).
/// Einstellungen und Protokolle liegen getrennt in ~/Library/Application Support und bleiben beim Aktualisieren unberührt.
/// </summary>
static class Installation
{
    const string AppName = Program.Name + ".app";
    const string MainDll = "XviewsPaperlessAiOcr.dll";

    // Texte für Installations-, Deinstallations- und Info-Fenster
    public static string InstallBody => T("Das Tool bekommt einen festen Platz im Ordner „Programme“ und erscheint im Launchpad. Autostart und Updates zeigen dann immer auf die richtige App.",
                                          "The tool gets a fixed place in the Applications folder and shows up in Launchpad. Autostart and updates then always point to the right app.");
    public static string NotInstalledHint => T("Installiert bekommt es einen festen Platz im Ordner „Programme“ und lässt sich hier sauber wieder entfernen.",
                                               "Installed, it gets a fixed place in the Applications folder and can be removed cleanly here.");
    public static string InstalledHint => T("Beim Deinstallieren kannst du wählen, ob Einstellungen und Protokolle bleiben.",
                                            "When uninstalling you can choose whether settings and logs stay.");
    public static string[] RemovedItems => [T("Programm", "Program"), "Autostart"];

    static readonly Lazy<string> folder = new(() =>
    {
        // /Applications gehört Administratoren; Standardbenutzer bekommen ~/Applications
        string system = "/Applications";
        try
        {
            string probe = Path.Combine(system, $".xviews-probe-{Environment.ProcessId}");
            File.WriteAllText(probe, "");
            File.Delete(probe);
            return system;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Applications");
        }
    });

    public static string Folder => folder.Value;
    static string InstalledApp => Path.Combine(Folder, AppName);
    public static string Exe => Path.Combine(InstalledApp, "Contents", "MacOS", "XviewsPaperlessAiOcr");
    static string CurrentExe => Environment.ProcessPath!;

    /// <summary>Die .app, aus der dieses Tool läuft; null beim Start aus dem Build-Ordner.</summary>
    public static string? CurrentApp
    {
        get
        {
            string? app = Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(CurrentExe)));
            return app is not null && app.EndsWith(".app", StringComparison.OrdinalIgnoreCase) ? app : null;
        }
    }

    /// <summary>Läuft gerade die App aus dem Programme-Ordner? Ohne .app (Entwicklung) gilt das Tool als installiert.</summary>
    public static bool RunningInstalled => CurrentApp is not { } app || SamePath(app, InstalledApp);

    public static bool IsInstalled => File.Exists(Exe);

    /// <summary>Version der installierten App aus ihrer Info.plist, null wenn nicht installiert.</summary>
    public static Version? InstalledVersion()
    {
        if (!IsInstalled) return null;
        try
        {
            string plist = File.ReadAllText(Path.Combine(InstalledApp, "Contents", "Info.plist"));
            Match m = Regex.Match(plist, @"<key>CFBundleShortVersionString</key>\s*<string>([^<]+)</string>");
            return m.Success && Version.TryParse(m.Groups[1].Value, out var v) ? new Version(v.Major, v.Minor, Math.Max(0, v.Build)) : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }
    }

    /// <summary>Ist die installierte App genau dieser Stand? Verglichen wird die Programm-dll, die exe ist bei jedem Build gleich.</summary>
    public static bool InstalledIsSame()
    {
        string installed = Path.Combine(InstalledApp, "Contents", "MacOS", MainDll);
        string current = Path.Combine(AppContext.BaseDirectory, MainDll);
        if (!File.Exists(installed) || !File.Exists(current)) return false;
        if (new FileInfo(installed).Length != new FileInfo(current).Length) return false;
        using var a = File.OpenRead(installed);
        using var b = File.OpenRead(current);
        return SHA256.HashData(a).AsSpan().SequenceEqual(SHA256.HashData(b));
    }

    /// <summary>Kopiert diese App in den Programme-Ordner und ersetzt dort eine ältere.</summary>
    public static void Install()
    {
        if (CurrentApp is not { } app || SamePath(app, InstalledApp)) return;
        Directory.CreateDirectory(Folder);
        if (Directory.Exists(InstalledApp)) Directory.Delete(InstalledApp, recursive: true);
        // ditto behält Rechte, Signatur und Symlinks. Ohne Quarantäne-Kennzeichen: der Nutzer hat diese App schon geöffnet.
        Run("/usr/bin/ditto", "--noqtn", app, InstalledApp);
        Log.Write($"Installiert in {InstalledApp}");
    }

    /// <summary>Beim Start der installierten App: ein vorhandener Autostart zeigt auf sie.</summary>
    public static void Refresh()
    {
        try { Autostart.UpdatePath(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Write($"Installation nicht aufgefrischt: {e.Message}");
        }
    }

    /// <summary>Entfernt Autostart und App, auf Wunsch auch Einstellungen und Protokolle. Unter macOS darf eine laufende App gelöscht werden.</summary>
    public static void Uninstall(bool deleteData)
    {
        Autostart.Remove();
        if (Directory.Exists(InstalledApp)) Directory.Delete(InstalledApp, recursive: true);
        if (deleteData && Directory.Exists(Settings.Folder)) Directory.Delete(Settings.Folder, recursive: true);
        Log.Write("Deinstalliert");
    }

    /// <summary>Öffnet das Installationsfenster in einem eigenen Prozess (Knopf im Info-Fenster).</summary>
    public static void StartInstaller() =>
        Process.Start(new ProcessStartInfo(CurrentExe, "--installieren") { UseShellExecute = false })?.Dispose();

    /// <summary>Öffnet das Deinstallationsfenster der installierten App.</summary>
    public static void StartUninstaller() =>
        Process.Start(new ProcessStartInfo(File.Exists(Exe) ? Exe : CurrentExe, "--deinstallieren") { UseShellExecute = false })?.Dispose();

    /// <summary>Startet die installierte App mit denselben Argumenten.</summary>
    public static void StartInstalled(IEnumerable<string> args) => OpenApp(InstalledApp, args.Where(a => a != "--installieren"));

    /// <summary>Startet eine .app über LaunchServices als neue Instanz (open -n), damit Dock und Menüleiste sie richtig zuordnen.</summary>
    public static void OpenApp(string app, IEnumerable<string> args) =>
        Run("/usr/bin/open", ["-n", app, "--args", .. args], wait: false);

    static void Run(string file, params string[] args) => Run(file, args, wait: true);

    static void Run(string file, IEnumerable<string> args, bool wait)
    {
        var psi = new ProcessStartInfo(file) { UseShellExecute = false, RedirectStandardError = wait };
        foreach (string a in args) psi.ArgumentList.Add(a);
        using Process p = Process.Start(psi) ?? throw new IOException(T($"{file} ließ sich nicht starten.", $"Could not start {file}."));
        if (!wait) return;
        string error = p.StandardError.ReadToEnd();
        p.WaitForExit();
        if (p.ExitCode != 0) throw new IOException($"{Path.GetFileName(file)}: {error.Trim()}");
    }

    static bool SamePath(string a, string b) =>
        string.Equals(Path.GetFullPath(a).TrimEnd('/'), Path.GetFullPath(b).TrimEnd('/'), StringComparison.OrdinalIgnoreCase);
}
