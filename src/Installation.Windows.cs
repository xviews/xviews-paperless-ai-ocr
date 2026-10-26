// SPDX-License-Identifier: MIT
// Copyright (C) 2026 Xviews

using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;

namespace XviewsPaperlessAiOcr;

/// <summary>
/// Windows-Installation wie beim ClaDuo Profil-Manager: fester Ort (%LOCALAPPDATA%\Programs\Xviews Paperless AI-OCR),
/// Startmenü-Ordner mit „deinstallieren“, Eintrag unter „Installierte Apps“. Keine Adminrechte nötig.
/// Einstellungen und Protokolle liegen getrennt in %APPDATA% und bleiben beim Aktualisieren unberührt.
/// </summary>
static class Installation
{
    public const string ExeName = "XviewsPaperlessAiOcr.exe";
    const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\XviewsPaperlessAiOcr";

    public static string Folder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", Program.Name);
    public static string Exe => Path.Combine(Folder, ExeName);
    static string CurrentExe => Environment.ProcessPath!;
    static string StartMenuFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), Program.Name);
    static string StartMenuApp => Path.Combine(StartMenuFolder, Program.Name + ".lnk");
    /// <summary>Kurzer Name, damit er sich im Startmenü (wird abgeschnitten) vom Tool unterscheidet.</summary>
    static string StartMenuUninstall => Path.Combine(StartMenuFolder, "Paperless AI-OCR deinstallieren.lnk");
    /// <summary>Eigenes Symbol mit rotem Minus für den Deinstallieren-Eintrag, liegt im Programmordner.</summary>
    static string UninstallIcon => Path.Combine(Folder, "deinstall.ico");

    // Texte für Installations-, Deinstallations- und Info-Fenster
    public static string InstallBody => T("Das Tool bekommt einen festen Platz, einen Eintrag im Startmenü und unter „Installierte Apps“. Autostart und Updates zeigen dann immer auf die richtige Datei.",
                                          "The tool gets a fixed place, an entry in the Start menu and under “Installed apps”. Autostart and updates then always point to the right file.");
    public static string NotInstalledHint => T("Installiert bekommt es einen festen Platz, einen Eintrag im Startmenü und unter „Installierte Apps“ und lässt sich dort sauber wieder entfernen.",
                                               "Installed, it gets a fixed place, an entry in the Start menu and under “Installed apps”, and can be removed cleanly there.");
    public static string InstalledHint => T("Mit Eintrag im Startmenü und unter „Installierte Apps“. Beim Deinstallieren kannst du wählen, ob Einstellungen und Protokolle bleiben.",
                                            "With an entry in the Start menu and under “Installed apps”. When uninstalling you can choose whether settings and logs stay.");
    public static string[] RemovedItems => [T("Programm", "Program"), T("Startmenü-Ordner", "Start menu folder"), "Autostart", T("Eintrag „Installierte Apps“", "“Installed apps” entry")];

    /// <summary>Läuft gerade die exe aus dem Installationsordner? Ein fehlender Eintrag wird dann beim Start repariert.</summary>
    public static bool RunningInstalled => SamePath(CurrentExe, Exe);

    /// <summary>Installiert = exe am festen Ort und Eintrag unter „Installierte Apps“.</summary>
    public static bool IsInstalled => Registered && File.Exists(Exe);

    static bool Registered
    {
        get
        {
            try { using var k = Registry.CurrentUser.OpenSubKey(UninstallKey); return k is not null; }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException) { return false; }
        }
    }

    /// <summary>Version der installierten exe, null wenn nicht installiert.</summary>
    public static Version? InstalledVersion()
    {
        if (!IsInstalled) return null;
        var info = FileVersionInfo.GetVersionInfo(Exe);
        return Version.TryParse(info.FileVersion, out var v) ? new Version(v.Major, v.Minor, Math.Max(0, v.Build)) : null;
    }

    /// <summary>Ist die installierte exe genau diese Datei (gleiche Version und gleicher Build)?</summary>
    public static bool InstalledIsSame()
    {
        if (!IsInstalled) return false;
        if (new FileInfo(Exe).Length != new FileInfo(CurrentExe).Length) return false;
        using var a = File.OpenRead(Exe);
        using var b = File.OpenRead(CurrentExe);
        return SHA256.HashData(a).AsSpan().SequenceEqual(SHA256.HashData(b));
    }

    /// <summary>Kopiert diese exe an den festen Ort und legt Startmenü und Deinstallations-Eintrag an.</summary>
    public static void Install()
    {
        Directory.CreateDirectory(Folder);
        if (!SamePath(CurrentExe, Exe))
        {
            // Eine laufende exe lässt sich umbenennen, aber nicht überschreiben
            string old = Exe + ".alt";
            try { File.Delete(old); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            if (File.Exists(Exe)) File.Move(Exe, old);
            File.Copy(CurrentExe, Exe);
            try { File.Delete(old); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
        CreateStartMenu();
        Register();
        Log.Write($"Installiert in {Folder}");
    }

    /// <summary>Beim Start der installierten exe: Reste eines Updates entfernen, Einträge auf den neuesten Stand bringen.</summary>
    public static void Refresh()
    {
        try { File.Delete(Exe + ".alt"); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        try
        {
            if (!File.Exists(StartMenuApp) || !File.Exists(StartMenuUninstall) || !File.Exists(UninstallIcon)) CreateStartMenu();
            Register();
            Autostart.UpdatePath();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or COMException)
        {
            Log.Write($"Installation nicht aufgefrischt: {e.Message}");
        }
    }

    static void Register()
    {
        using var k = Registry.CurrentUser.CreateSubKey(UninstallKey);
        Version v = UpdateChecker.Current;
        k.SetValue("DisplayName", Program.Name);
        k.SetValue("DisplayVersion", $"{v.Major}.{v.Minor}.{v.Build}");
        k.SetValue("Publisher", "Xviews");
        k.SetValue("DisplayIcon", $"\"{Exe}\",0");
        k.SetValue("InstallLocation", Folder);
        k.SetValue("URLInfoAbout", InfoWindow.ProjectUrl);
        k.SetValue("UninstallString", $"\"{Exe}\" --deinstallieren");
        k.SetValue("NoModify", 1, RegistryValueKind.DWord);
        k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
        k.SetValue("EstimatedSize", (int)(new FileInfo(Exe).Length / 1024), RegistryValueKind.DWord);
    }

    static void CreateStartMenu()
    {
        Directory.CreateDirectory(StartMenuFolder);
        CreateShortcut(StartMenuApp, Exe, "", Exe, T("Lokale KI-Texterkennung für Paperless-ngx", "Local AI text recognition for Paperless-ngx"));
        string icon = Exe;
        try
        {
            using Stream source = typeof(Installation).Assembly.GetManifestResourceStream("deinstall.ico")!;
            using FileStream target = File.Create(UninstallIcon);
            source.CopyTo(target);
            icon = UninstallIcon;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        CreateShortcut(StartMenuUninstall, Exe, "--deinstallieren", icon, T("Tool entfernen", "Remove tool"));
    }

    /// <summary>
    /// Entfernt Startmenü, Autostart, Deinstallations-Eintrag und den Programmordner,
    /// auf Wunsch auch Einstellungen und Protokolle.
    /// </summary>
    public static void Uninstall(bool deleteData)
    {
        Autostart.Remove();
        try { Directory.Delete(StartMenuFolder, recursive: true); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, throwOnMissingSubKey: false);

        var folders = new List<string> { Folder };
        if (deleteData) folders.Add(Settings.Folder);
        foreach (string f in folders.ToList())
            try { Directory.Delete(f, recursive: true); folders.Remove(f); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            catch (DirectoryNotFoundException) { folders.Remove(f); }

        // Die laufende exe kann sich nicht selbst löschen: PowerShell wartet, bis dieser Prozess
        // beendet ist, und löscht dann die restlichen Ordner. Windows oder der Virenscanner geben die
        // exe manchmal erst etwas später frei, darum bis zu einer Minute lang erneut versuchen.
        if (folders.Count > 0)
        {
            string list = string.Join(",", folders.Select(f => $"'{f.Replace("'", "''")}'"));
            Process.Start(new ProcessStartInfo("powershell.exe",
                $"-NoProfile -NonInteractive -WindowStyle Hidden -Command \"Wait-Process -Id {Environment.ProcessId} -Timeout 3600 -ErrorAction SilentlyContinue; " +
                $"$f = @({list}); for ($i = 0; $i -lt 60; $i++) {{ Start-Sleep -Seconds 1; " +
                "Remove-Item -LiteralPath $f -Recurse -Force -ErrorAction SilentlyContinue; " +
                "if (-not ($f | Where-Object { Test-Path -LiteralPath $_ })) { break } }\"")
            // Nicht im Programmordner starten (Arbeitsordner der Startmenü-Verknüpfung), sonst sperrt das Skript ihn selbst
            { CreateNoWindow = true, UseShellExecute = false, WorkingDirectory = Path.GetTempPath() })?.Dispose();
        }
    }

    /// <summary>Öffnet das Installationsfenster in einem eigenen Prozess (Knopf im Info-Fenster).</summary>
    public static void StartInstaller() =>
        Process.Start(new ProcessStartInfo(CurrentExe, "--installieren") { UseShellExecute = false })?.Dispose();

    /// <summary>Öffnet das Deinstallationsfenster der installierten exe.</summary>
    public static void StartUninstaller() =>
        Process.Start(new ProcessStartInfo(File.Exists(Exe) ? Exe : CurrentExe, "--deinstallieren") { UseShellExecute = false })?.Dispose();

    /// <summary>Startet die installierte exe mit denselben Argumenten.</summary>
    public static void StartInstalled(IEnumerable<string> args)
    {
        var psi = new ProcessStartInfo(Exe) { UseShellExecute = false, WorkingDirectory = Folder };
        foreach (string a in args.Where(a => a != "--installieren")) psi.ArgumentList.Add(a);
        Process.Start(psi)?.Dispose();
    }

    static bool SamePath(string a, string b) =>
        string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);

    // --- Verknüpfungen (IShellLinkW) -------------------------------------------------------------

    static void CreateShortcut(string lnk, string target, string arguments, string icon, string description)
    {
        var link = (IShellLinkW)new ShellLink();
        link.SetPath(target);
        link.SetArguments(arguments);
        link.SetWorkingDirectory(Path.GetDirectoryName(target)!);
        link.SetDescription(description);
        link.SetIconLocation(icon, 0);
        ((IPersistFile)link).Save(lnk, true);
    }

    [ComImport, Guid("000214F9-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder file, int max, nint fd, uint flags);
        void GetIDList(out nint pidl);
        void SetIDList(nint pidl);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder name, int max);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string name);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder dir, int max);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string dir);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder args, int max);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string args);
        void GetHotkey(out short hotkey);
        void SetHotkey(short hotkey);
        void GetShowCmd(out int showCmd);
        void SetShowCmd(int showCmd);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder path, int max, out int icon);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string path, int icon);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string path, uint reserved);
        void Resolve(nint hwnd, uint flags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string file);
    }

    [ComImport, Guid("0000010b-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IPersistFile
    {
        void GetClassID(out Guid classId);
        [PreserveSig] int IsDirty();
        void Load([MarshalAs(UnmanagedType.LPWStr)] string file, uint mode);
        void Save([MarshalAs(UnmanagedType.LPWStr)] string file, [MarshalAs(UnmanagedType.Bool)] bool remember);
        void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string file);
        void GetCurFile([MarshalAs(UnmanagedType.LPWStr)] out string file);
    }

    [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
    class ShellLink { }
}
