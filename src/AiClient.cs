// SPDX-License-Identifier: MIT
// Copyright (C) 2026 Xviews

using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace XviewsPaperlessAiOcr;

/// <summary>
/// Lässt eine Seite von einem Vision-Modell abschreiben, über Ollama oder einen OpenAI-kompatiblen
/// Server (LM Studio, llama.cpp, vLLM, LocalAI …).
/// </summary>
public sealed partial class AiClient(Settings settings)
{
    public const string Ollama = "ollama", OpenAi = "openai";

    /// <summary>Wählbare Server mit ihrer üblichen Adresse.</summary>
    public static readonly (string Id, string Name, string DefaultUrl)[] Servers =
    [
        (Ollama, "Ollama", "http://127.0.0.1:11434"),
        (OpenAi, "OpenAI-kompatibel (LM Studio, llama.cpp, vLLM …)", "http://127.0.0.1:1234"),
    ];

    const string Prompt =
        "Du bist eine OCR-Engine. Schreibe den gesamten Text dieser Dokumentseite exakt so ab, " +
        "wie er auf der Seite steht: in Lesereihenfolge, Zeile für Zeile, mit Originalschreibweise, " +
        "Zahlen, Datumsangaben und Satzzeichen. Übersetze nichts, fasse nichts zusammen, korrigiere " +
        "nichts und füge keine Kommentare, Überschriften oder Markdown hinzu. Bei Tabellen schreibe " +
        "die Zellen einer Zeile in eine Textzeile, getrennt durch zwei Leerzeichen. Unleserliche " +
        "Stellen lässt du weg. Ist die Seite leer, antworte mit nichts.";

    readonly HttpClient http = new() { Timeout = TimeSpan.FromSeconds(settings.PageTimeoutSeconds) };

    bool IsOllama => settings.Server != OpenAi;
    string Base => settings.ServerUrl.TrimEnd('/');
    string ServerName => IsOllama ? "Ollama" : "Der KI-Server";

    public async Task<string> ReadPageAsync(byte[] png, CancellationToken ct)
    {
        await EnsureServerAsync(ct);
        if (settings.Model == "")
        {
            // Kein Modell gewählt: das erste mit Bilderkennung nehmen und merken
            settings.Model = (await VisionModelsAsync(ct)).FirstOrDefault()
                ?? throw new InvalidOperationException(T("Auf dem KI-Server gibt es kein Modell mit Bilderkennung (Vision).", "The AI server has no model with image recognition (vision)."));
            settings.Save();
            Log.Write($"Kein Modell gewählt, nehme {settings.Model}");
        }
        string image = Convert.ToBase64String(png);
        return Clean(IsOllama ? await ChatOllamaAsync(image, ct) : await ChatOpenAiAsync(image, ct));
    }

    async Task<string> ChatOllamaAsync(string image, CancellationToken ct)
    {
        var request = new
        {
            model = settings.Model,
            stream = false,
            think = false,
            options = new { temperature = 0, num_ctx = 16384 },
            messages = new[] { new { role = "user", content = Prompt, images = new[] { image } } },
        };
        using var response = await http.PostAsJsonAsync($"{Base}/api/chat", request, ct);
        string body = await response.Content.ReadAsStringAsync(ct);
        if (response.StatusCode == HttpStatusCode.NotFound && body.Contains("not found"))
            throw new InvalidOperationException(MissingModel);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Ollama {(int)response.StatusCode}: {body}");
        using var json = JsonDocument.Parse(body);
        return json.RootElement.GetProperty("message").GetProperty("content").GetString() ?? "";
    }

    async Task<string> ChatOpenAiAsync(string image, CancellationToken ct)
    {
        var request = new
        {
            model = settings.Model,
            stream = false,
            temperature = 0,
            messages = new[]
            {
                new
                {
                    role = "user",
                    content = new object[]
                    {
                        new { type = "text", text = Prompt },
                        new { type = "image_url", image_url = new { url = $"data:image/png;base64,{image}" } },
                    },
                },
            },
        };
        using var response = await http.PostAsJsonAsync($"{Base}/v1/chat/completions", request, ct);
        string body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(response.StatusCode == HttpStatusCode.NotFound && body.Contains("model", StringComparison.OrdinalIgnoreCase)
                ? MissingModel : $"KI-Server {(int)response.StatusCode}: {body}");
        using var json = JsonDocument.Parse(body);
        return json.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? "";
    }

    /// <summary>Entfernt Denk-Abschnitte, Markdown-Codeblöcke und überflüssige Leerzeilen.</summary>
    static string Clean(string text)
    {
        text = Thinking().Replace(text, "");
        text = CodeFence().Replace(text, "").Replace("\r\n", "\n").Trim();
        return MultiBlank().Replace(text, "\n\n");
    }

    [GeneratedRegex(@"<think>.*?</think>", RegexOptions.Singleline)]
    private static partial Regex Thinking();

    [GeneratedRegex(@"^```[a-zA-Z]*\s*$", RegexOptions.Multiline)]
    private static partial Regex CodeFence();

    [GeneratedRegex(@"\n{3,}")]
    private static partial Regex MultiBlank();

    /// <summary>Prüft den Server; Ollama wird bei Bedarf gestartet (bis zu 60 s Wartezeit).</summary>
    /// <param name="manual">True, wenn der Nutzer es selbst angestoßen hat; dann auch mit ausgeschaltetem Automatikstart.</param>
    public async Task EnsureServerAsync(CancellationToken ct, bool manual = false)
    {
        if (await IsUpAsync(ct)) return;
        if (!IsOllama)
            throw new InvalidOperationException($"Der KI-Server unter {Base} antwortet nicht. Bitte starten (LM Studio: Developer → Start Server).");
        if (!settings.StartOllamaOnDemand && !manual)
            throw new InvalidOperationException(T("Ollama läuft nicht. Das automatische Starten ist ausgeschaltet, bitte Ollama von Hand starten.", "Ollama is not running. Automatic start is switched off, please start Ollama by hand."));
        // Unter macOS ist die App ein Ordner (Ollama.app)
        if (!File.Exists(settings.OllamaAppPath) && !Directory.Exists(settings.OllamaAppPath))
            throw new InvalidOperationException(T($"Ollama läuft nicht und ist nicht installiert ({settings.OllamaAppPath} fehlt).", $"Ollama is not running and not installed ({settings.OllamaAppPath} is missing).") + " Download: https://ollama.com/download");
        Log.Write("Ollama läuft nicht, starte Ollama-App.");
        Process.Start(new ProcessStartInfo(settings.OllamaAppPath) { UseShellExecute = true });
        StartedByUs = true;
        for (int i = 0; i < 60; i++)
        {
            await Task.Delay(1000, ct);
            if (await IsUpAsync(ct)) return;
        }
        throw new InvalidOperationException(T("Ollama wurde gestartet, antwortet aber nicht. Bitte Ollama einmal von Hand starten.", "Ollama was started but does not answer. Please start Ollama once by hand."));
    }

    /// <summary>True, wenn dieses Tool Ollama gestartet hat. Nur dann wird es bei Leerlauf wieder beendet.</summary>
    public bool StartedByUs { get; private set; }

    /// <summary>Beendet die Ollama-App samt Server, die dieses Tool gestartet hat; gibt Grafik- und Arbeitsspeicher frei.</summary>
    public void StopOllama()
    {
        if (!StartedByUs) return;
        StartedByUs = false;
#if MAC
        // Server und Hilfsprogramme liegen in Ollama.app/Contents/Resources
        string folder = settings.OllamaAppPath;
        string[] names = ["Ollama", "ollama", "llama-server"];
#else
        string folder = Path.GetDirectoryName(settings.OllamaAppPath) ?? "";
        string[] names = ["ollama app", "ollama", "llama-server"];
#endif
        foreach (string name in names)
            foreach (Process p in Process.GetProcessesByName(name))
                try
                {
                    if (p.MainModule?.FileName.StartsWith(folder, StringComparison.OrdinalIgnoreCase) == true) p.Kill(true);
                }
                catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception) { }
                finally { p.Dispose(); }
        Log.Write("Ollama nach Leerlauf beendet.");
    }

    string MissingModel => IsOllama
        ? T($"Das Modell „{settings.Model}“ fehlt in Ollama. Einmal {(OperatingSystem.IsMacOS() ? "im Terminal" : "in der Eingabeaufforderung")} ausführen: ollama pull {settings.Model}",
            $"The model “{settings.Model}” is missing in Ollama. Run once {(OperatingSystem.IsMacOS() ? "in Terminal" : "in the command prompt")}: ollama pull {settings.Model}")
        : T($"Das Modell „{settings.Model}“ gibt es auf dem KI-Server nicht. Bitte im Info-Fenster ein anderes wählen.",
            $"The model “{settings.Model}” does not exist on the AI server. Please choose another one in the info window.");

    /// <summary>Modelle, die Bilder lesen können.</summary>
    public async Task<List<string>> VisionModelsAsync(CancellationToken ct)
    {
        var result = new List<string>();
        if (IsOllama)
        {
            // Ollama meldet die Fähigkeit "vision" pro Modell
            using var tags = JsonDocument.Parse(await http.GetStringAsync($"{Base}/api/tags", ct));
            foreach (JsonElement m in tags.RootElement.GetProperty("models").EnumerateArray())
            {
                string name = m.GetProperty("name").GetString() ?? "";
                using var response = await http.PostAsJsonAsync($"{Base}/api/show", new { model = name }, ct);
                if (!response.IsSuccessStatusCode) continue;
                using var show = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
                if (show.RootElement.TryGetProperty("capabilities", out JsonElement caps)
                    && caps.EnumerateArray().Any(c => c.GetString() == "vision"))
                    result.Add(name);
            }
            return result;
        }

        // LM Studio kennzeichnet Vision-Modelle in seiner eigenen Liste mit type "vlm"
        try
        {
            using var lm = JsonDocument.Parse(await http.GetStringAsync($"{Base}/api/v0/models", ct));
            foreach (JsonElement m in lm.RootElement.GetProperty("data").EnumerateArray())
                if (m.TryGetProperty("type", out JsonElement type) && type.GetString() == "vlm")
                    result.Add(m.GetProperty("id").GetString() ?? "");
            return result;
        }
        catch (Exception e) when (e is HttpRequestException or JsonException or KeyNotFoundException) { }

        // Andere Server sagen nicht, welche Modelle Bilder können, dann alle anbieten
        return await AllModelsAsync(ct);
    }

    async Task<List<string>> AllModelsAsync(CancellationToken ct)
    {
        if (IsOllama)
        {
            using var tags = JsonDocument.Parse(await http.GetStringAsync($"{Base}/api/tags", ct));
            return [.. tags.RootElement.GetProperty("models").EnumerateArray().Select(m => m.GetProperty("name").GetString() ?? "")];
        }
        using var models = JsonDocument.Parse(await http.GetStringAsync($"{Base}/v1/models", ct));
        return [.. models.RootElement.GetProperty("data").EnumerateArray().Select(m => m.GetProperty("id").GetString() ?? "")];
    }

    /// <summary>Kurzer Status für das Info-Fenster, ohne Ollama zu starten.</summary>
    public async Task<(bool Ok, string Text)> DescribeAsync()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        try
        {
            if (!await IsUpAsync(cts.Token))
                return !IsOllama ? (false, T($"Kein KI-Server unter {Base} erreichbar.", $"No AI server reachable at {Base}."))
                    : !File.Exists(settings.OllamaAppPath) ? (false, T("Ollama ist nicht installiert.", "Ollama is not installed.") + " Download: https://ollama.com/download")
                    : settings.StartOllamaOnDemand ? (true, T("Ollama startet automatisch, sobald Paperless ein Dokument schickt.", "Ollama starts automatically as soon as Paperless sends a document."))
                    : (false, T("Ollama läuft nicht und startet nicht automatisch. Bitte von Hand starten oder unten den Schalter einschalten.", "Ollama is not running and does not start automatically. Please start it by hand or switch on the option below."));
            if (settings.Model == "")
                return (false, T("Noch kein Modell gewählt.", "No model selected yet."));
            return (await AllModelsAsync(cts.Token)).Contains(settings.Model)
                ? (true, T($"Verbunden, Modell „{settings.Model}“ ist bereit.", $"Connected, model “{settings.Model}” is ready.")) : (false, MissingModel);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException or KeyNotFoundException)
        {
            return (false, $"{ServerName} antwortet nicht richtig: {e.Message}");
        }
    }

    /// <summary>Name für kurze Statuszeilen: „Ollama“ oder „KI-Server“.</summary>
    public string ShortName => IsOllama ? "Ollama" : T("KI-Server", "AI server");

    public bool StartsOnDemand => IsOllama && settings.StartOllamaOnDemand;

    /// <summary>Läuft der KI-Server gerade? Startet nichts, wartet höchstens zwei Sekunden.</summary>
    public async Task<bool> IsRunningAsync()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        try { return await IsUpAsync(cts.Token); }
        catch (TaskCanceledException) { return false; }
    }

    async Task<bool> IsUpAsync(CancellationToken ct)
    {
        try
        {
            using var r = await http.GetAsync(IsOllama ? $"{Base}/api/version" : $"{Base}/v1/models", ct);
            return r.IsSuccessStatusCode;
        }
        catch (HttpRequestException) { return false; }
    }
}
