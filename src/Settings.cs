// SPDX-License-Identifier: MIT
// Copyright (C) 2026 Xviews

using System.Text.Json;
using System.Text.Json.Serialization;

namespace XviewsPaperlessAiOcr;

/// <summary>Einstellungen in %APPDATA%\Xviews Paperless AI-OCR\einstellungen.json (Mac: ~/Library/Application Support/…).</summary>
public sealed class Settings
{
    public int Port { get; set; } = 5050;
    public string ApiKey { get; set; } = "";
    /// <summary>"ollama" oder "openai" (LM Studio, llama.cpp, vLLM …).</summary>
    public string Server { get; set; } = AiClient.Ollama;
    public string ServerUrl { get; set; } = "http://127.0.0.1:11434";
#if MAC
    public string OllamaAppPath { get; set; } = "/Applications/Ollama.app";
#else
    public string OllamaAppPath { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Ollama", "ollama app.exe");
#endif
    /// <summary>Vision-Modell auf dem KI-Server; leer = das erste installierte Modell mit Bilderkennung nehmen.</summary>
    public string Model { get; set; } = "";
    public int RenderDpi { get; set; } = 200;
    public int PageTimeoutSeconds { get; set; } = 300;
    /// <summary>Zweiter Versuch, wenn Ollama „token repeat limit reached“ meldet. Nicht im Info-Fenster, nur hier.</summary>
    public double RetryTemperature { get; set; } = 0.3;
    public double RetryRepeatPenalty { get; set; } = 1.15;
    public bool NotifyWhenDone { get; set; } = true;
    public bool NotifyErrors { get; set; } = true;
    public bool StartOllamaOnDemand { get; set; } = true;
    public bool StopOllamaWhenIdle { get; set; } = true;
    public int OllamaIdleMinutes { get; set; } = 15;
    /// <summary>Paperless hat schon einmal ein Dokument erfolgreich lesen lassen.</summary>
    public bool PaperlessConnected { get; set; }

    public string LastUpdateCheck { get; set; } = "";
    public string LatestVersion { get; set; } = "";
    public string LatestUrl { get; set; } = "";

    /// <summary>True, wenn es noch keine Einstellungen gab. Dann zeigt das Tool die Einrichtung.</summary>
    [JsonIgnore] public bool FirstStart { get; private set; }

#if MAC
    public static string Folder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support", "Xviews Paperless AI-OCR");
#else
    public static string Folder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Xviews Paperless AI-OCR");
#endif
    static string FilePath => Path.Combine(Folder, "einstellungen.json");

    // Vorversionen speicherten appsettings.json neben der EXE
    static string OldFilePath => Path.Combine(AppContext.BaseDirectory, "appsettings.json");

    static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    static readonly object Gate = new();

    public static Settings Load()
    {
        string? source = File.Exists(FilePath) ? FilePath : File.Exists(OldFilePath) ? OldFilePath : null;
        string text = source is null ? "" : File.ReadAllText(source);
        Settings s = source is null ? new Settings() : JsonSerializer.Deserialize<Settings>(text) ?? new Settings();
        s.FirstStart = source is null;

        // Beim ersten Start einen zufälligen Schlüssel erzeugen, den Paperless als "API-Key" mitschickt.
        bool newKey = string.IsNullOrWhiteSpace(s.ApiKey);
        if (newKey)
            s.ApiKey = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
        // Neue Felder gleich hineinschreiben, damit man sie in der Datei findet
        if (newKey || source != FilePath || !text.Contains(nameof(RetryRepeatPenalty))) s.Save();
        return s;
    }

    /// <summary>Nach jedem Speichern, damit das Menü am Symbol aktuell bleibt (kann aus dem Hintergrund kommen).</summary>
    public event Action? Saved;

    public void Save()
    {
        lock (Gate)
        {
            Directory.CreateDirectory(Folder);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Json));
        }
        Saved?.Invoke();
    }
}
