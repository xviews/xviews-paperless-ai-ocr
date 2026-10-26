// SPDX-License-Identifier: MIT
// Copyright (C) 2026 Xviews

using System.Text;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Writer;
using PigDocument = UglyToad.PdfPig.PdfDocument;

namespace XviewsPaperlessAiOcr;

/// <summary>
/// Baut das "durchsuchbare PDF": Originalseiten (oder Bilder) mit einer unsichtbaren Textebene.
/// Das Modell liefert keine Koordinaten, darum werden die Zeilen gleichmäßig über die Seite verteilt:
/// Suche und Kopieren funktionieren, die Markierung liegt aber nur ungefähr über dem Text.
/// </summary>
public static class PdfBuilder
{
    static readonly Lazy<byte[]> Arial = new(() =>
    {
        // Windows: C:\Windows\Fonts, macOS: Systemschriften unter /System/Library/Fonts/Supplemental
        string[] candidates =
        [
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "arial.ttf"),
            "/System/Library/Fonts/Supplemental/Arial.ttf",
            "/Library/Fonts/Arial.ttf",
        ];
        string path = candidates.FirstOrDefault(File.Exists) ?? throw new FileNotFoundException("Schrift Arial nicht gefunden.", candidates[0]);
        return File.ReadAllBytes(path);
    });

    public static byte[] Build(byte[] original, IReadOnlyList<PageImage> pages, IReadOnlyList<string> texts)
    {
        if (PageRenderer.IsPdf(original))
        {
            try { return FromPdf(original, texts); }
            catch (Exception e) when (e is not OutOfMemoryException)
            {
                Log.Write($"Original-PDF nicht übernehmbar ({e.Message}), baue PDF aus Seitenbildern.");
            }
        }
        return FromImages(original, pages, texts);
    }

    /// <summary>Übernimmt die Originalseiten, damit Qualität und Vektorinhalt erhalten bleiben.</summary>
    static byte[] FromPdf(byte[] original, IReadOnlyList<string> texts)
    {
        using var input = PigDocument.Open(original);
        using var builder = new PdfDocumentBuilder();
        var font = new Font(builder);
        for (int i = 1; i <= input.NumberOfPages; i++)
        {
            var source = input.GetPage(i);
            var page = builder.AddPage(input, i);
            AddText(page, source.Width, source.Height, i <= texts.Count ? texts[i - 1] : "", font);
        }
        return builder.Build();
    }

    static byte[] FromImages(byte[] original, IReadOnlyList<PageImage> pages, IReadOnlyList<string> texts)
    {
        using var builder = new PdfDocumentBuilder();
        var font = new Font(builder);
        for (int i = 0; i < pages.Count; i++)
        {
            var p = pages[i];
            var page = builder.AddPage(p.WidthPt, p.HeightPt);
            var area = new PdfRectangle(0, 0, p.WidthPt, p.HeightPt);
            // Ein JPEG-Foto unverändert übernehmen, als PNG würde es deutlich größer
            if (pages.Count == 1 && IsJpeg(original)) page.AddJpeg(original, area);
            else page.AddPng(p.Png, area);
            AddText(page, p.WidthPt, p.HeightPt, i < texts.Count ? texts[i] : "", font);
        }
        return builder.Build();
    }

    static bool IsJpeg(byte[] data) => data.Length > 3 && data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF;

    /// <summary>Schreibt die Zeilen im PDF-Textmodus „unsichtbar“, so machen es auch OCRmyPDF und Tesseract.</summary>
    static void AddText(PdfPageBuilder page, double w, double h, string text, Font font)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        var lines = text.Split('\n');
        double margin = Math.Min(w, h) * 0.06;
        double lineHeight = Math.Clamp((h - 2 * margin) / lines.Length, 4, 14);

        page.SetTextRenderingMode(TextRenderingMode.Neither);
        for (int l = 0; l < lines.Length; l++)
        {
            double y = margin + (l + 1) * lineHeight;
            if (y > h - margin / 2) break;
            string line = font.Printable(page, lines[l]);
            if (line.Trim().Length > 0)
                page.AddText(line, lineHeight * 0.8, new PdfPoint(margin, h - y), font.Added);
        }
        page.SetTextRenderingMode(TextRenderingMode.Fill);
    }

    /// <summary>Arial aus den Systemschriften; Zeichen ohne Glyphe (Emojis, chinesische Schrift …) werden ausgelassen.</summary>
    sealed class Font(PdfDocumentBuilder builder)
    {
        public readonly PdfDocumentBuilder.AddedFont Added = builder.AddTrueTypeFont(Arial.Value);
        readonly Dictionary<char, bool> known = [];

        public string Printable(PdfPageBuilder page, string line)
        {
            var sb = new StringBuilder(line.Length);
            foreach (char c in line)
            {
                if (char.IsSurrogate(c) || char.IsControl(c) && c != '\t') continue;
                if (!known.TryGetValue(c, out bool ok))
                {
                    try { page.MeasureText(c.ToString(), 10, new PdfPoint(0, 0), Added); ok = true; }
                    catch (InvalidOperationException) { ok = false; }
                    known[c] = ok;
                }
                if (ok) sb.Append(c);
            }
            return sb.ToString();
        }
    }
}
