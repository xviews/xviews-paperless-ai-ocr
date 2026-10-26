# Zeichnet das Logo (gleiche Geometrie wie logo.svg: Dokument mit Scan-Linie im Scan-Rahmen)
# und erzeugt logo-512.png (Mac-Symbol), logo-128.png (Fenster) sowie app.ico und deinstall.ico.
Add-Type -AssemblyName System.Drawing
$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot

function New-RoundRect([float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = 2 * $r
    $p.AddArc($x, $y, $d, $d, 180, 90); $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90); $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $p.CloseFigure(); $p
}

function Draw-Logo([int]$size, [bool]$badge = $false) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'; $g.PixelOffsetMode = 'HighQuality'
    # Raster 128, Kachel 120 mit 4 Rand
    $g.ScaleTransform($size / 128.0, $size / 128.0)
    $g.TranslateTransform(4, 4)
    $c = { param($hex) [System.Drawing.ColorTranslator]::FromHtml($hex) }
    $P = { param($x, $y) [System.Drawing.PointF]::new($x, $y) }

    # Dunkle Kachel
    $g.FillPath((New-Object System.Drawing.SolidBrush (& $c '#10261a')), (New-RoundRect 0 0 120 120 26))

    # Scan-Rahmen: vier grüne Ecken
    $frame = New-Object System.Drawing.Pen (& $c '#41d17a'), 5
    $frame.StartCap = 'Round'; $frame.EndCap = 'Round'; $frame.LineJoin = 'Round'
    foreach ($corner in @(@(18, 38, 18, 18, 38, 18), @(82, 18, 102, 18, 102, 38), @(102, 82, 102, 102, 82, 102), @(38, 102, 18, 102, 18, 82))) {
        $cp = New-Object System.Drawing.Drawing2D.GraphicsPath
        $cp.AddLines([System.Drawing.PointF[]]@((& $P $corner[0] $corner[1]), (& $P $corner[2] $corner[3]), (& $P $corner[4] $corner[5])))
        $g.DrawPath($frame, $cp)
    }

    # Dokument mit Eselsohr
    $doc = New-Object System.Drawing.Drawing2D.GraphicsPath
    $doc.AddLines([System.Drawing.PointF[]]@((& $P 42 30), (& $P 68 30), (& $P 82 44), (& $P 82 88)))
    $doc.AddArc(74, 84, 8, 8, 0, 90); $doc.AddArc(38, 84, 8, 8, 90, 90); $doc.AddArc(38, 30, 8, 8, 180, 90); $doc.CloseFigure()
    $g.FillPath([System.Drawing.Brushes]::White, $doc)
    $ear = New-Object System.Drawing.Drawing2D.GraphicsPath
    $ear.AddLines([System.Drawing.PointF[]]@((& $P 68 30), (& $P 68 40), (& $P 72 44), (& $P 82 44))); $ear.CloseFigure()
    $g.FillPath((New-Object System.Drawing.SolidBrush (& $c '#cfe8d6')), $ear)

    # Textzeilen
    $dark = New-Object System.Drawing.SolidBrush (& $c '#1f7a3a'); $light = New-Object System.Drawing.SolidBrush (& $c '#8fbf9c')
    $g.FillPath($dark, (New-RoundRect 44 52 28 4 2))
    foreach ($l in @(@(61, 22), @(70, 26), @(79, 16))) { $g.FillPath($light, (New-RoundRect 44 $l[0] $l[1] 4 2)) }

    # Scan-Linie
    $g.FillPath((New-Object System.Drawing.SolidBrush (& $c '#41d17a')), (New-RoundRect 26 65 68 3 1.5))

    if ($badge) {
        # Rotes Minus-Abzeichen unten rechts wie beim ClaDuo Profil-Manager
        $rand = New-Object System.Drawing.Pen (& $c '#10261a'), 6
        $g.DrawEllipse($rand, 68, 68, 50, 50); $g.FillEllipse((New-Object System.Drawing.SolidBrush (& $c '#d83b2f')), 68, 68, 50, 50)
        $g.FillRectangle([System.Drawing.Brushes]::White, 79, 89, 28, 8)
    }
    else {
        # KI-Funke (vierzackiger Stern) an der Ecke des Blatts
        $s = New-Object System.Drawing.Drawing2D.GraphicsPath
        $s.AddBezier(82, 70, 84, 82, 84, 82, 96, 84); $s.AddBezier(96, 84, 84, 86, 84, 86, 82, 98)
        $s.AddBezier(82, 98, 80, 86, 80, 86, 68, 84); $s.AddBezier(68, 84, 80, 82, 80, 82, 82, 70); $s.CloseFigure()
        $pen = New-Object System.Drawing.Pen (& $c '#10261a'), 2; $pen.LineJoin = 'Round'
        $g.DrawPath($pen, $s); $g.FillPath((New-Object System.Drawing.SolidBrush (& $c '#ffc23d')), $s)
    }
    $g.Dispose(); $bmp
}

# PNGs
foreach ($sz in 512, 128) { $b = Draw-Logo $sz; $b.Save((Join-Path $here "logo-$sz.png"), [System.Drawing.Imaging.ImageFormat]::Png); $b.Dispose() }

# ICO mit PNG-Einträgen (16 bis 256 px): app.ico und deinstall.ico (mit rotem Minus für den Startmenü-Eintrag)
function Write-Ico([string]$name, [bool]$badge) {
    $sizes = 16, 24, 32, 48, 64, 128, 256
    $images = foreach ($sz in $sizes) { $b = Draw-Logo $sz $badge; $ms = New-Object System.IO.MemoryStream; $b.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png); $b.Dispose(); , $ms.ToArray() }
    $out = New-Object System.IO.MemoryStream; $w = New-Object System.IO.BinaryWriter $out
    $w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$sizes.Count)
    $offset = 6 + 16 * $sizes.Count
    for ($i = 0; $i -lt $sizes.Count; $i++) {
        $dim = if ($sizes[$i] -ge 256) { 0 } else { $sizes[$i] }
        $w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0)
        $w.Write([uint16]1); $w.Write([uint16]32); $w.Write([uint32]$images[$i].Length); $w.Write([uint32]$offset)
        $offset += $images[$i].Length
    }
    foreach ($img in $images) { $w.Write($img) }
    [System.IO.File]::WriteAllBytes((Join-Path $here $name), $out.ToArray())
}
Write-Ico 'app.ico' $false
Write-Ico 'deinstall.ico' $true
"Fertig: logo-512.png, logo-128.png, app.ico, deinstall.ico"
