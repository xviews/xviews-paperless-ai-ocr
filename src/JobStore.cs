// SPDX-License-Identifier: MIT
// Copyright (C) 2026 Xviews

using System.Collections.Concurrent;

namespace XviewsPaperlessAiOcr;

public enum JobStatus { NotStarted, Running, Succeeded, Failed }

public sealed class OcrJob
{
    public string Id { get; } = Guid.NewGuid().ToString();
    public DateTime Created { get; } = DateTime.UtcNow;
    public DateTime Updated { get; set; } = DateTime.UtcNow;
    public JobStatus Status { get; set; } = JobStatus.NotStarted;
    public string Content { get; set; } = "";
    public List<(double W, double H, int Offset, int Length)> Pages { get; } = [];
    public byte[]? Pdf { get; set; }
    public string? Error { get; set; }
    /// <summary>Seitenzahl, sobald das Dokument in Bilder zerlegt ist (für den Fortschritt im Statusfenster).</summary>
    public int PageCount { get; set; }
    /// <summary>IP-Adresse, von der Paperless das Dokument geschickt hat.</summary>
    public string From { get; init; } = "";
}

public enum ContactResult { WrongKey, Accepted, Succeeded, Failed }

/// <summary>Letzte Anfrage von Paperless, für die Statuszeile im Info-Fenster.</summary>
public sealed record PaperlessContact(DateTime Time, string From, ContactResult Result, int Pages = 0, string? Error = null, TimeSpan Duration = default);

/// <summary>Hält Aufträge im Speicher und arbeitet sie nacheinander ab (eine GPU, ein Auftrag).</summary>
public sealed class JobStore(Settings settings)
{
    readonly ConcurrentDictionary<string, OcrJob> jobs = new();
    readonly SemaphoreSlim gpu = new(1, 1);
    readonly AiClient ocr = new(settings);

    /// <summary>Meldet eine fehlgeschlagene Texterkennung (läuft nicht im UI-Thread).</summary>
    public event Action<string>? JobFailed;

    /// <summary>Meldet eine Anfrage mit falschem API-Schlüssel und deren Absender (läuft nicht im UI-Thread).</summary>
    public event Action<string>? WrongKey;

    DateTime lastWrongKeyLog = DateTime.MinValue;

    /// <summary>Anfrage mit falschem Schlüssel: merken, höchstens alle 10 Minuten protokollieren und melden.</summary>
    public void RejectWrongKey(string from)
    {
        SetContact(new PaperlessContact(DateTime.Now, from, ContactResult.WrongKey));
        if (DateTime.Now - lastWrongKeyLog < TimeSpan.FromMinutes(10)) return;
        lastWrongKeyLog = DateTime.Now;
        Log.Write($"Anfrage von {from} mit falschem API-Schlüssel abgewiesen");
        WrongKey?.Invoke(from);
    }

    /// <summary>Meldet ein fertiges Dokument mit Seitenzahl und Dauer (läuft nicht im UI-Thread).</summary>
    public event Action<int, TimeSpan>? JobSucceeded;

    /// <summary>Letzter Kontakt von Paperless hat sich geändert (läuft nicht im UI-Thread).</summary>
    public event Action? ContactChanged;

    /// <summary>Letzte Anfrage von Paperless seit dem Start, null wenn noch keine kam.</summary>
    public PaperlessContact? LastContact { get; private set; }

    public void SetContact(PaperlessContact contact)
    {
        LastContact = contact;
        // Einmal geklappt: das Info-Fenster klappt die Paperless-Anleitung ab dann zu
        if (contact.Result == ContactResult.Succeeded && !settings.PaperlessConnected)
        {
            settings.PaperlessConnected = true;
            settings.Save();
        }
        ContactChanged?.Invoke();
    }

    public AiClient Ocr => ocr;

    /// <summary>Ende des letzten Auftrags (oder Programmstart), für das Beenden von Ollama bei Leerlauf.</summary>
    public DateTime LastActivity { get; private set; } = DateTime.Now;

    public int Active =>jobs.Values.Count(j => j.Status is JobStatus.NotStarted or JobStatus.Running);

    /// <summary>Der Auftrag, der gerade gelesen wird (es läuft immer nur einer).</summary>
    public OcrJob? Running => jobs.Values.FirstOrDefault(j => j.Status == JobStatus.Running);

    int recognized;

    /// <summary>Erkannte Dokumente seit dem Start, nur im Speicher gezählt.</summary>
    public int Recognized => recognized;

    public OcrJob? Get(string id) => jobs.TryGetValue(id, out var j) ? j : null;

    public OcrJob Start(byte[] document, string from)
    {
        Cleanup();
        var job = new OcrJob { From = from };
        jobs[job.Id] = job;
        SetContact(new PaperlessContact(DateTime.Now, from, ContactResult.Accepted));
        _ = Task.Run(() => RunAsync(job, document));
        return job;
    }

    async Task RunAsync(OcrJob job, byte[] document)
    {
        await gpu.WaitAsync();
        try
        {
            job.Status = JobStatus.Running;
            var started = DateTime.Now;
            var pages = await PageRenderer.RenderAsync(document, settings.RenderDpi);
            job.PageCount = pages.Count;
            Log.Write($"Auftrag {job.Id[..8]}: {pages.Count} Seite(n), {document.Length / 1024} KB");

            var texts = new List<string>();
            var content = new System.Text.StringBuilder();
            for (int i = 0; i < pages.Count; i++)
            {
                var t0 = DateTime.Now;
                var text = await ocr.ReadPageAsync(pages[i].Png, CancellationToken.None);
                texts.Add(text);
                if (i > 0) content.Append('\n');
                int offset = content.Length;
                content.Append(text);
                job.Pages.Add((pages[i].WidthPt / 72.0, pages[i].HeightPt / 72.0, offset, text.Length));
                job.Updated = DateTime.UtcNow;
                Log.Write($"  Seite {i + 1}/{pages.Count}: {text.Length} Zeichen in {(DateTime.Now - t0).TotalSeconds:F1}s");
            }

            job.Content = content.ToString();
            job.Pdf = PdfBuilder.Build(document, pages, texts);
            job.Status = JobStatus.Succeeded;
            Log.Write($"Auftrag {job.Id[..8]} fertig in {(DateTime.Now - started).TotalSeconds:F0}s");
            Interlocked.Increment(ref recognized);
            SetContact(new PaperlessContact(DateTime.Now, job.From, ContactResult.Succeeded, pages.Count, Duration: DateTime.Now - started));
            JobSucceeded?.Invoke(pages.Count, DateTime.Now - started);
        }
        catch (Exception e)
        {
            job.Error = string.IsNullOrWhiteSpace(e.Message) ? $"{e.GetType().Name}. Details im Protokoll." : e.Message;
            job.Status = JobStatus.Failed;
            Log.Write($"Auftrag {job.Id[..8]} FEHLER: {e}");
            SetContact(new PaperlessContact(DateTime.Now, job.From, ContactResult.Failed, Error: job.Error));
            JobFailed?.Invoke(job.Error);
        }
        finally
        {
            job.Updated = DateTime.UtcNow;
            LastActivity = DateTime.Now;
            gpu.Release();
        }
    }

    /// <summary>Fertige Aufträge nach einer Stunde vergessen.</summary>
    void Cleanup()
    {
        foreach (var (id, j) in jobs)
            if (j.Status is JobStatus.Succeeded or JobStatus.Failed && DateTime.UtcNow - j.Updated > TimeSpan.FromHours(1))
                jobs.TryRemove(id, out _);
    }
}
