# Draws the Price Tracker app icon and writes src\PriceTracker\Assets\app.ico (multi-size, PNG entries)
# plus app-256.png / app-512.png (used in the README and inside the app).
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$root = Split-Path $PSScriptRoot -Parent
$assets = Join-Path $root 'src\PriceTracker\Assets'
New-Item -ItemType Directory -Force $assets | Out-Null

function New-RoundRect([single]$x, [single]$y, [single]$w, [single]$h, [single]$r) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $r * 2
    $p.AddArc($x, $y, $d, $d, 180, 90)
    $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $p.CloseFigure()
    return $p
}

function C([int]$a, [int]$r, [int]$g, [int]$b) { [System.Drawing.Color]::FromArgb($a, $r, $g, $b) }
function P([double]$x, [double]$y) { [System.Drawing.PointF]::new([single]$x, [single]$y) }

function Draw-Icon([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.InterpolationMode = 'HighQualityBicubic'
    $g.PixelOffsetMode = 'HighQuality'
    $s = [double]$size

    # Squircle tile: fresh green -> teal gradient (Apple-like).
    $m = $s * 0.04
    $tile = New-RoundRect $m $m ($s - 2 * $m) ($s - 2 * $m) ($s * 0.225)
    $grad = New-Object System.Drawing.Drawing2D.LinearGradientBrush (P 0 0), (P $s $s), (C 255 52 211 153), (C 255 13 148 136)
    $g.FillPath($grad, $tile)
    # Soft top highlight.
    $hl = New-Object System.Drawing.Drawing2D.LinearGradientBrush (P 0 (-1)), (P 0 ($s + 1)), (C 60 255 255 255), (C 0 255 255 255)
    $g.FillPath($hl, $tile)

    # Price tag (rotated rounded shape with a hole), white.
    $state = $g.Save()
    $g.TranslateTransform([single]($s * 0.43), [single]($s * 0.41))
    $g.RotateTransform(-45)
    $tw = $s * 0.48; $th = $s * 0.29
    $tag = New-Object System.Drawing.Drawing2D.GraphicsPath
    $r = $th * 0.18
    $x0 = -$tw / 2; $y0 = -$th / 2
    $tip = $th * 0.5
    $tag.AddLine((P ($x0 + $tip) $y0), (P ($x0 + $tw - $r) $y0))
    $tag.AddArc([single]($x0 + $tw - 2 * $r), [single]$y0, [single](2 * $r), [single](2 * $r), 270, 90)
    $tag.AddArc([single]($x0 + $tw - 2 * $r), [single]($y0 + $th - 2 * $r), [single](2 * $r), [single](2 * $r), 0, 90)
    $tag.AddLine((P ($x0 + $tw - $r) ($y0 + $th)), (P ($x0 + $tip) ($y0 + $th)))
    $tag.AddLine((P ($x0 + $tip) ($y0 + $th)), (P $x0 0))
    $tag.CloseFigure()
    $white = New-Object System.Drawing.SolidBrush (C 255 255 255 255)
    $g.FillPath($white, $tag)
    $hole = $th * 0.22
    $holeBrush = New-Object System.Drawing.SolidBrush (C 255 20 160 140)
    $g.FillEllipse($holeBrush, [single]($x0 + $tip * 0.95 - $hole / 2), [single](-$hole / 2), [single]$hole, [single]$hole)
    $g.Restore($state)

    # Downward trend arrow across the lower right (the "price drop").
    $pw = [Math]::Max(1.4, $s * 0.055)
    $pen = New-Object System.Drawing.Pen (C 255 255 255 255), ([single]$pw)
    $pen.StartCap = 'Round'; $pen.EndCap = 'Round'; $pen.LineJoin = 'Round'
    $shadow = New-Object System.Drawing.Pen (C 60 0 60 50), ([single]($pw * 1.9))
    $shadow.StartCap = 'Round'; $shadow.EndCap = 'Round'; $shadow.LineJoin = 'Round'
    $pts = [System.Drawing.PointF[]]@((P ($s * 0.52) ($s * 0.66)), (P ($s * 0.62) ($s * 0.76)), (P ($s * 0.69) ($s * 0.69)), (P ($s * 0.80) ($s * 0.80)))
    $g.DrawLines($shadow, $pts)
    $g.DrawLines($pen, $pts)
    $a = $s * 0.075
    $head = [System.Drawing.PointF[]]@((P ($s * 0.80 - $a) ($s * 0.80)), (P ($s * 0.80) ($s * 0.80)), (P ($s * 0.80) ($s * 0.80 - $a)))
    $g.DrawLines($pen, $head)

    $g.Dispose()
    return $bmp
}

$sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256
$pngs = @()
foreach ($sz in $sizes) {
    $bmp = Draw-Icon $sz
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    if ($sz -eq 256) { $bmp.Save((Join-Path $assets 'app-256.png'), [System.Drawing.Imaging.ImageFormat]::Png) }
    $bmp.Dispose()
    $pngs += , $ms.ToArray()
}
$big = Draw-Icon 512
$big.Save((Join-Path $assets 'app-512.png'), [System.Drawing.Imaging.ImageFormat]::Png)
$big.Dispose()

# ICO container with PNG-compressed entries.
$out = New-Object System.IO.MemoryStream
$w = New-Object System.IO.BinaryWriter $out
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $sz = $sizes[$i]; $len = $pngs[$i].Length
    $dim = if ($sz -ge 256) { 0 } else { $sz }
    $w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([uint16]1); $w.Write([uint16]32); $w.Write([uint32]$len); $w.Write([uint32]$offset)
    $offset += $len
}
foreach ($p in $pngs) { $w.Write($p) }
$w.Flush()
[System.IO.File]::WriteAllBytes((Join-Path $assets 'app.ico'), $out.ToArray())
Write-Output "Wrote $assets\app.ico"
