// SPDX-License-Identifier: MIT
// Copyright (C) 2026 Xviews

global using static XviewsPaperlessAiOcr.Lang;
using System.Diagnostics;
using System.Globalization;

namespace XviewsPaperlessAiOcr;

/// <summary>
/// Sprache der Oberfläche: Deutsch, wenn das System auf Deutsch steht, sonst Englisch.
/// Texte stehen direkt im Code als T("deutsch", "english").
/// </summary>
public static class Lang
{
    public static bool English { get; } = !SystemLanguage().StartsWith("de", StringComparison.OrdinalIgnoreCase);

    public static string T(string de, string en) => English ? en : de;

    static string SystemLanguage()
    {
#if MAC
        // Aus dem Finder gestartet hat die App kein LANG, .NET sähe dann nur Englisch. Die Sprachliste von macOS fragen.
        try
        {
            var psi = new ProcessStartInfo("/usr/bin/defaults") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (string a in new[] { "read", "-g", "AppleLanguages" }) psi.ArgumentList.Add(a);
            using Process? p = Process.Start(psi);
            if (p is not null)
            {
                string output = p.StandardOutput.ReadToEnd();
                p.WaitForExit(3000);
                // Ausgabe wie ( "de-DE", "en-DE" ): die erste Sprache zählt
                int start = output.IndexOf('"');
                int end = start < 0 ? -1 : output.IndexOf('"', start + 1);
                if (end > start) return output[(start + 1)..end];
            }
        }
        catch (Exception e) when (e is IOException or System.ComponentModel.Win32Exception) { }
#endif
        return CultureInfo.CurrentUICulture.Name;
    }
}
