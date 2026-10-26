// SPDX-License-Identifier: MIT
// Copyright (C) 2026 Xviews

using Microsoft.Win32;

namespace XviewsPaperlessAiOcr;

/// <summary>Start mit Windows über HKCU\...\Run (nur für den eigenen Benutzer, keine Adminrechte nötig).</summary>
static class Autostart
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string ValueName = Program.Name;
    public static string Label => T("Mit Windows starten", "Start with Windows");

    static string ExePath => Environment.ProcessPath!;
    static string Command => $"\"{ExePath}\" --autostart";

    /// <summary>Nach dem Umschalten, damit das Menü am Symbol aktuell bleibt.</summary>
    public static event Action? Changed;

    public static bool Enabled
    {
        get
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) is string value
                && value.Contains(ExePath, StringComparison.OrdinalIgnoreCase);
        }
        set
        {
            using RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (value) key.SetValue(ValueName, Command);
            else key.DeleteValue(ValueName, false);
            Log.Write(value ? "Autostart eingeschaltet" : "Autostart ausgeschaltet");
            Changed?.Invoke();
        }
    }

    /// <summary>Nach dem Installieren: ein vorhandener Autostart zeigt auf diese exe statt auf die alte Datei.</summary>
    public static void UpdatePath()
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        if (key?.GetValue(ValueName) is string value && !value.Contains(ExePath, StringComparison.OrdinalIgnoreCase))
            key.SetValue(ValueName, Command);
    }

    public static void Remove()
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        key?.DeleteValue(ValueName, false);
    }
}
