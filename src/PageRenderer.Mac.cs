// SPDX-License-Identifier: MIT
// Copyright (C) 2026 Xviews

using System.Diagnostics;
using System.Runtime.InteropServices;
using SkiaSharp;

namespace XviewsPaperlessAiOcr;

/// <summary>
/// Mac: PDFs rendert PDFium (libpdfium.dylib aus bblanchon.PDFium.macOS), Bilder liest SkiaSharp, das Avalonia ohnehin
/// mitbringt. Was Skia nicht kennt (TIFF …), wandelt das macOS-Bordmittel sips um, bei mehrseitigen TIFFs nur die erste Seite.
/// </summary>
public static partial class PageRenderer
{
    // PDFium ist nicht threadsicher
    static readonly object Gate = new();
    static bool initialized;

    public static Task<List<PageImage>> RenderAsync(byte[] data, int dpi) => Task.Run(() =>
    {
        try
        {
            return IsPdf(data) ? RenderPdf(data, dpi) : RenderImage(data);
        }
        catch (Exception e) when (e is IOException or ArgumentException or InvalidOperationException or DllNotFoundException)
        {
            throw new InvalidOperationException(
                T($"Das Dokument ließ sich nicht öffnen ({(IsPdf(data) ? "PDF" : "kein PDF und kein lesbares Bild")}, {data.Length / 1024} KB).",
                  $"Could not open the document ({(IsPdf(data) ? "PDF" : "not a PDF and no readable image")}, {data.Length / 1024} KB)."), e);
        }
    });

    static unsafe List<PageImage> RenderPdf(byte[] data, int dpi)
    {
        var pages = new List<PageImage>();
        lock (Gate)
        {
            if (!initialized)
            {
                Pdfium.FPDF_InitLibrary();
                initialized = true;
            }
            fixed (byte* bytes = data)
            {
                // Die Bytes müssen gepinnt bleiben, solange das Dokument offen ist
                nint doc = Pdfium.FPDF_LoadMemDocument(bytes, data.Length, null);
                if (doc == 0) throw new InvalidOperationException($"PDFium kann das PDF nicht lesen (Fehler {Pdfium.FPDF_GetLastError()}).");
                try
                {
                    int count = Pdfium.FPDF_GetPageCount(doc);
                    for (int i = 0; i < count; i++)
                    {
                        nint page = Pdfium.FPDF_LoadPage(doc, i);
                        if (page == 0) throw new InvalidOperationException($"PDFium kann Seite {i + 1} nicht laden.");
                        try
                        {
                            double wPt = Pdfium.FPDF_GetPageWidthF(page), hPt = Pdfium.FPDF_GetPageHeightF(page);
                            int w = Math.Max(1, (int)Math.Round(wPt / 72.0 * dpi)), h = Math.Max(1, (int)Math.Round(hPt / 72.0 * dpi));
                            pages.Add(new PageImage(RenderPage(page, w, h), wPt, hPt));
                        }
                        finally { Pdfium.FPDF_ClosePage(page); }
                    }
                }
                finally { Pdfium.FPDF_CloseDocument(doc); }
            }
        }
        return pages;
    }

    static byte[] RenderPage(nint page, int w, int h)
    {
        nint bitmap = Pdfium.FPDFBitmap_Create(w, h, 0);
        if (bitmap == 0) throw new InvalidOperationException(T($"Kein Speicher für ein Seitenbild mit {w} × {h} Pixeln.", $"Not enough memory for a page image of {w} × {h} pixels."));
        try
        {
            Pdfium.FPDFBitmap_FillRect(bitmap, 0, 0, w, h, 0xFFFFFFFF);
            Pdfium.FPDF_RenderPageBitmap(bitmap, page, 0, 0, w, h, 0, Pdfium.FPDF_ANNOT);
            // BGRx: ein Byte pro Kanal, das vierte ist ohne Bedeutung
            var info = new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Opaque);
            using SKImage image = SKImage.FromPixels(info, Pdfium.FPDFBitmap_GetBuffer(bitmap), Pdfium.FPDFBitmap_GetStride(bitmap));
            using SKData png = image.Encode(SKEncodedImageFormat.Png, 100);
            return png.ToArray();
        }
        finally { Pdfium.FPDFBitmap_Destroy(bitmap); }
    }

    static List<PageImage> RenderImage(byte[] data)
    {
        using SKBitmap? bitmap = SKBitmap.Decode(data) ?? (ConvertWithSips(data) is { Length: > 0 } converted ? SKBitmap.Decode(converted) : null);
        if (bitmap is null) throw new InvalidOperationException("Unbekanntes Bildformat.");
        using SKData png = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        // Skia liefert keine Auflösung, darum wie unter Windows bei Bildern ohne Angabe 300 dpi annehmen
        return [new PageImage(png.ToArray(), bitmap.Width * 72.0 / 300, bitmap.Height * 72.0 / 300)];
    }

    /// <summary>Wandelt ein Bild mit sips in PNG um; leeres Array, wenn das nicht klappt.</summary>
    static byte[] ConvertWithSips(byte[] data)
    {
        string dir = Directory.CreateTempSubdirectory("xviews-ocr-").FullName;
        try
        {
            string input = Path.Combine(dir, "eingang"), output = Path.Combine(dir, "seite.png");
            File.WriteAllBytes(input, data);
            var psi = new ProcessStartInfo("/usr/bin/sips") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (string a in new[] { "-s", "format", "png", input, "--out", output }) psi.ArgumentList.Add(a);
            using Process? p = Process.Start(psi);
            if (p is null) return [];
            p.StandardOutput.ReadToEnd();
            p.StandardError.ReadToEnd();
            if (!p.WaitForExit(60000)) { p.Kill(); return []; }
            return p.ExitCode == 0 && File.Exists(output) ? File.ReadAllBytes(output) : [];
        }
        catch (Exception e) when (e is IOException or System.ComponentModel.Win32Exception) { return []; }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch (IOException) { }
        }
    }

    /// <summary>Die wenigen PDFium-Funktionen, die das Tool braucht (fpdfview.h).</summary>
    static unsafe partial class Pdfium
    {
        const string Lib = "pdfium";
        public const int FPDF_ANNOT = 0x01;

        [LibraryImport(Lib)] public static partial void FPDF_InitLibrary();
        [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)] public static partial nint FPDF_LoadMemDocument(byte* data, int size, string? password);
        [LibraryImport(Lib)] public static partial uint FPDF_GetLastError();
        [LibraryImport(Lib)] public static partial int FPDF_GetPageCount(nint doc);
        [LibraryImport(Lib)] public static partial nint FPDF_LoadPage(nint doc, int index);
        [LibraryImport(Lib)] public static partial float FPDF_GetPageWidthF(nint page);
        [LibraryImport(Lib)] public static partial float FPDF_GetPageHeightF(nint page);
        [LibraryImport(Lib)] public static partial void FPDF_ClosePage(nint page);
        [LibraryImport(Lib)] public static partial void FPDF_CloseDocument(nint doc);
        [LibraryImport(Lib)] public static partial nint FPDFBitmap_Create(int width, int height, int alpha);
        [LibraryImport(Lib)] public static partial int FPDFBitmap_FillRect(nint bitmap, int left, int top, int width, int height, uint color);
        [LibraryImport(Lib)] public static partial void FPDF_RenderPageBitmap(nint bitmap, nint page, int x, int y, int w, int h, int rotate, int flags);
        [LibraryImport(Lib)] public static partial nint FPDFBitmap_GetBuffer(nint bitmap);
        [LibraryImport(Lib)] public static partial int FPDFBitmap_GetStride(nint bitmap);
        [LibraryImport(Lib)] public static partial void FPDFBitmap_Destroy(nint bitmap);
    }
}
