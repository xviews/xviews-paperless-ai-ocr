// SPDX-License-Identifier: MIT
// Copyright (C) 2026 Xviews

using Windows.Data.Pdf;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace XviewsPaperlessAiOcr;

/// <summary>Wandelt PDFs und Bilder mit Windows-Bordmitteln (Windows.Data.Pdf / WIC) in Seitenbilder um.</summary>
public static partial class PageRenderer
{
    public static async Task<List<PageImage>> RenderAsync(byte[] data, int dpi)
    {
        try
        {
            return IsPdf(data) ? await RenderPdfAsync(data, dpi) : await RenderImageAsync(data);
        }
        catch (Exception e) when (e is System.Runtime.InteropServices.COMException or ArgumentException)
        {
            throw new InvalidOperationException(
                T($"Das Dokument ließ sich nicht öffnen ({(IsPdf(data) ? "PDF" : "kein PDF und kein lesbares Bild")}, {data.Length / 1024} KB).",
                  $"Could not open the document ({(IsPdf(data) ? "PDF" : "not a PDF and no readable image")}, {data.Length / 1024} KB)."), e);
        }
    }

    static async Task<IRandomAccessStream> ToStreamAsync(byte[] data)
    {
        var ras = new InMemoryRandomAccessStream();
        using (var writer = new DataWriter(ras.GetOutputStreamAt(0)))
        {
            writer.WriteBytes(data);
            await writer.StoreAsync();
            await writer.FlushAsync();
            writer.DetachStream();
        }
        ras.Seek(0);
        return ras;
    }

    static async Task<byte[]> ReadAllAsync(IRandomAccessStream stream)
    {
        var bytes = new byte[stream.Size];
        using var reader = new DataReader(stream.GetInputStreamAt(0));
        await reader.LoadAsync((uint)stream.Size);
        reader.ReadBytes(bytes);
        return bytes;
    }

    static async Task<List<PageImage>> RenderPdfAsync(byte[] data, int dpi)
    {
        using var input = await ToStreamAsync(data);
        var doc = await PdfDocument.LoadFromStreamAsync(input);
        var pages = new List<PageImage>();
        for (uint i = 0; i < doc.PageCount; i++)
        {
            using var page = doc.GetPage(i);
            // page.Size ist in DIPs (1/96 Zoll)
            double wPt = page.Size.Width * 72.0 / 96.0, hPt = page.Size.Height * 72.0 / 96.0;
            var options = new PdfPageRenderOptions
            {
                DestinationWidth = (uint)Math.Round(page.Size.Width / 96.0 * dpi),
                BitmapEncoderId = BitmapEncoder.PngEncoderId,
            };
            using var output = new InMemoryRandomAccessStream();
            await page.RenderToStreamAsync(output, options);
            pages.Add(new PageImage(await ReadAllAsync(output), wPt, hPt));
        }
        return pages;
    }

    static async Task<List<PageImage>> RenderImageAsync(byte[] data)
    {
        using var input = await ToStreamAsync(data);
        var decoder = await BitmapDecoder.CreateAsync(input);
        var pages = new List<PageImage>();
        for (uint i = 0; i < decoder.FrameCount; i++)
        {
            var frame = await decoder.GetFrameAsync(i);
            using var bitmap = SoftwareBitmap.Convert(
                await frame.GetSoftwareBitmapAsync(), BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);

            using var output = new InMemoryRandomAccessStream();
            var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, output);
            encoder.SetSoftwareBitmap(bitmap);
            await encoder.FlushAsync();

            double dpiX = frame.DpiX > 1 ? frame.DpiX : 300, dpiY = frame.DpiY > 1 ? frame.DpiY : 300;
            pages.Add(new PageImage(await ReadAllAsync(output),
                bitmap.PixelWidth * 72.0 / dpiX, bitmap.PixelHeight * 72.0 / dpiY));
        }
        return pages;
    }
}
