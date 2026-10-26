// SPDX-License-Identifier: MIT
// Copyright (C) 2026 Xviews

using System.Security;

namespace XviewsPaperlessAiOcr;

/// <summary>Start beim Anmelden über einen LaunchAgent in ~/Library/LaunchAgents (nur für den eigenen Benutzer).</summary>
static class Autostart
{
    public static string Label => T("Beim Anmelden starten", "Start at login");
    const string AgentLabel = "eu.xviews.paperless-ai-ocr";

    static string ExePath => Environment.ProcessPath!;
    static string AgentFile => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "LaunchAgents", AgentLabel + ".plist");

    /// <summary>Nach dem Umschalten, damit das Menü am Symbol aktuell bleibt.</summary>
    public static event Action? Changed;

    public static bool Enabled
    {
        get
        {
            try { return File.Exists(AgentFile) && File.ReadAllText(AgentFile).Contains(SecurityElement.Escape(ExePath)); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return false; }
        }
        set
        {
            if (value) Write();
            else Remove();
            Log.Write(value ? "Autostart eingeschaltet" : "Autostart ausgeschaltet");
            Changed?.Invoke();
        }
    }

    /// <summary>Nach dem Installieren oder Verschieben: ein vorhandener Autostart zeigt auf diese App statt auf die alte.</summary>
    public static void UpdatePath()
    {
        if (File.Exists(AgentFile) && !Enabled) Write();
    }

    public static void Remove()
    {
        try { File.Delete(AgentFile); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    // macOS liest die Datei beim nächsten Anmelden, launchctl ist dafür nicht nötig
    static void Write()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(AgentFile)!);
        File.WriteAllText(AgentFile, $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
            <plist version="1.0">
            <dict>
              <key>Label</key>
              <string>{AgentLabel}</string>
              <key>ProgramArguments</key>
              <array>
                <string>{SecurityElement.Escape(ExePath)}</string>
                <string>--autostart</string>
              </array>
              <key>RunAtLoad</key>
              <true/>
              <key>LimitLoadToSessionType</key>
              <string>Aqua</string>
            </dict>
            </plist>

            """);
    }
}
