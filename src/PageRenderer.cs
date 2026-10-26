// SPDX-License-Identifier: MIT
// Copyright (C) 2026 Xviews

namespace XviewsPaperlessAiOcr;

/// <summary>Eine Dokumentseite als PNG plus ihre Größe in PDF-Punkten (1/72 Zoll).</summary>
public sealed record PageImage(byte[] Png, double WidthPt, double HeightPt);

/// <summary>Seitenbilder für das KI-Modell; RenderAsync steht in PageRenderer.Windows.cs bzw. PageRenderer.Mac.cs.</summary>
public static partial class PageRenderer
{
    public static bool IsPdf(byte[] data)
        => data.Length > 4 && data[0] == '%' && data[1] == 'P' && data[2] == 'D' && data[3] == 'F';
}
