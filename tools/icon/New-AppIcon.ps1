<#
.SYNOPSIS
    Renders the Codebase Explorer application icon (app-icon.ico + app-icon.png).

.DESCRIPTION
    Draws the icon with GDI+ (System.Drawing, Windows PowerShell 5.1 / Windows only) on a 256-unit
    grid that matches app-icon.svg, renders it at every size Windows uses for taskbar, Start menu,
    Explorer and desktop shortcuts, and packs the PNG frames into a multi-resolution .ico.
    The outputs are committed; rerun this script only after changing the design:

        powershell -ExecutionPolicy Bypass -File tools/icon/New-AppIcon.ps1
#>
[CmdletBinding()]
param(
    [string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'
if (-not $OutputDirectory) {
    # Resolved here: Windows PowerShell 5.1 leaves $PSScriptRoot empty in parameter defaults.
    $OutputDirectory = Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) '..\..\src\CodebaseExplorer.Desktop\Assets'
}
Add-Type -AssemblyName System.Drawing

function New-RoundedRect([single]$x, [single]$y, [single]$w, [single]$h, [single]$r) {
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = 2 * $r
    $path.AddArc($x, $y, $d, $d, 180, 90)
    $path.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $path.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $path.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $path.CloseFigure()
    return $path
}

function ColorOf([string]$hex, [int]$alpha = 255) {
    $c = [System.Drawing.ColorTranslator]::FromHtml($hex)
    return [System.Drawing.Color]::FromArgb($alpha, $c.R, $c.G, $c.B)
}

function Render-Icon([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)
    $g.ScaleTransform($size / 256.0, $size / 256.0)

    # Small sizes: thicker strokes and bigger nodes, and no cross edge, so the shape stays readable.
    $small = $size -le 32
    $stroke = if ($small) { 14 } else { 9 }
    $nodeScale = if ($small) { 1.25 } else { 1.0 }

    # Background: rounded square with an indigo -> violet diagonal gradient.
    $tile = New-RoundedRect 12 12 232 232 56
    $bg = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        (New-Object System.Drawing.PointF(12, 12)), (New-Object System.Drawing.PointF(244, 244)),
        (ColorOf '#4F46E5'), (ColorOf '#9333EA'))
    $blend = New-Object System.Drawing.Drawing2D.ColorBlend(3)
    $blend.Colors = @((ColorOf '#3B82F6'), (ColorOf '#6366F1'), (ColorOf '#9333EA'))
    $blend.Positions = @([single]0, [single]0.45, [single]1)
    $bg.InterpolationColors = $blend
    $g.FillPath($bg, $tile)

    # Soft top highlight for depth.
    $g.SetClip($tile)
    $hl = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        (New-Object System.Drawing.PointF(0, 12)), (New-Object System.Drawing.PointF(0, 140)),
        (ColorOf '#FFFFFF' 56), (ColorOf '#FFFFFF' 0))
    $g.FillRectangle($hl, 12, 13, 232, 124)
    $g.ResetClip()

    # Graph: root -> two modules -> three components, plus one cross dependency.
    $nodes = @{
        root  = @(128, 70, 22, '#FFFFFF')
        left  = @(84, 134, 17, '#FFFFFF')
        right = @(180, 134, 17, '#FFFFFF')
        a     = @(60, 194, 15, '#FFFFFF')
        b     = @(118, 194, 15, '#22D3EE')
        c     = @(184, 194, 15, '#F0ABFC')
    }
    $edges = @(@('root', 'left'), @('root', 'right'), @('left', 'a'), @('left', 'b'), @('right', 'c'))
    if (-not $small) { $edges += , @('right', 'b') }

    $pen = New-Object System.Drawing.Pen((ColorOf '#FFFFFF' 150), $stroke)
    $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    foreach ($e in $edges) {
        $p = $nodes[$e[0]]; $q = $nodes[$e[1]]
        $g.DrawLine($pen, [single]$p[0], [single]$p[1], [single]$q[0], [single]$q[1])
    }

    $shadow = New-Object System.Drawing.SolidBrush((ColorOf '#1E1B4B' 70))
    foreach ($n in $nodes.Values) {
        $r = $n[2] * $nodeScale
        $g.FillEllipse($shadow, [single]($n[0] - $r), [single]($n[1] - $r + 4), [single](2 * $r), [single](2 * $r))
    }
    foreach ($n in $nodes.Values) {
        $r = $n[2] * $nodeScale
        $brush = New-Object System.Drawing.SolidBrush((ColorOf $n[3]))
        $g.FillEllipse($brush, [single]($n[0] - $r), [single]($n[1] - $r), [single](2 * $r), [single](2 * $r))
        $brush.Dispose()
    }

    $g.Dispose()
    return $bmp
}

$OutputDirectory = [System.IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Force $OutputDirectory | Out-Null

$sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256
# Classic 32-bit BGRA DIB frame (BITMAPINFOHEADER + bottom-up pixels + empty AND mask).
function ConvertTo-DibFrame([System.Drawing.Bitmap]$bmp) {
    $s = $bmp.Width
    $ms = New-Object System.IO.MemoryStream
    $bw = New-Object System.IO.BinaryWriter($ms)
    $bw.Write([UInt32]40); $bw.Write([Int32]$s); $bw.Write([Int32]($s * 2))
    $bw.Write([UInt16]1); $bw.Write([UInt16]32); $bw.Write([UInt32]0)
    $bw.Write([UInt32]($s * $s * 4)); $bw.Write([Int32]0); $bw.Write([Int32]0)
    $bw.Write([UInt32]0); $bw.Write([UInt32]0)
    for ($y = $s - 1; $y -ge 0; $y--) {
        for ($x = 0; $x -lt $s; $x++) {
            $c = $bmp.GetPixel($x, $y)
            $bw.Write([byte]$c.B); $bw.Write([byte]$c.G); $bw.Write([byte]$c.R); $bw.Write([byte]$c.A)
        }
    }
    $maskRow = [int][Math]::Ceiling($s / 32.0) * 4
    $bw.Write((New-Object byte[] ($maskRow * $s)))
    $bw.Flush()
    return $ms.ToArray()
}

# Frames below 256 px are DIBs (read by every Windows API and System.Drawing); 256 px is PNG.
$frames = foreach ($s in $sizes) {
    $bmp = Render-Icon $s
    if ($s -ge 256) {
        $ms = New-Object System.IO.MemoryStream
        $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
        $bytes = $ms.ToArray()
    } else {
        $bytes = ConvertTo-DibFrame $bmp
    }
    $bmp.Dispose()
    , @($s, $bytes)
}

# ICO container: ICONDIR + one ICONDIRENTRY per frame.
$icoPath = Join-Path $OutputDirectory 'app-icon.ico'
$fs = [System.IO.File]::Create($icoPath)
$w = New-Object System.IO.BinaryWriter($fs)
$w.Write([UInt16]0); $w.Write([UInt16]1); $w.Write([UInt16]$frames.Count)
$offset = 6 + 16 * $frames.Count
foreach ($f in $frames) {
    $dim = if ($f[0] -ge 256) { 0 } else { $f[0] }
    $w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([UInt16]1); $w.Write([UInt16]32)
    $w.Write([UInt32]$f[1].Length); $w.Write([UInt32]$offset)
    $offset += $f[1].Length
}
foreach ($f in $frames) { $w.Write([byte[]]$f[1]) }
$w.Dispose()

# Large PNG for platforms that take a bitmap (Linux desktop entries, documentation).
$png = Render-Icon 512
$png.Save((Join-Path $OutputDirectory 'app-icon.png'), [System.Drawing.Imaging.ImageFormat]::Png)
$png.Dispose()

Write-Host "Wrote $icoPath ($($frames.Count) frames) and app-icon.png"
