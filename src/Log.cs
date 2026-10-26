// SPDX-License-Identifier: MIT
// Copyright (C) 2026 Xviews

namespace XviewsPaperlessAiOcr;

/// <summary>Einfaches Tages-Logfile im Unterordner "logs" des Einstellungsordners.</summary>
public static class Log
{
    static readonly object Gate = new();
    public static string Dir => Path.Combine(Settings.Folder, "logs");
    public static string CurrentFile => Path.Combine(Dir, $"ocr-{DateTime.Now:yyyy-MM-dd}.log");

    /// <summary>Schreibt eine Zeile; ein nicht beschreibbares Protokoll darf das Tool nie aufhalten.</summary>
    public static void Write(string message)
    {
        lock (Gate)
        {
            try
            {
                Directory.CreateDirectory(Dir);
                File.AppendAllText(CurrentFile, $"{DateTime.Now:HH:mm:ss} {message}{Environment.NewLine}");
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
    }
}
