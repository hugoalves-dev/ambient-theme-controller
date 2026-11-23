<#
.SYNOPSIS
  Regenerates the Windows executable icon from the brand logo geometry (docs/brand/logo.svg).
.DESCRIPTION
  Draws the 120x120 logo (8 rays, sun r=30, moon r=24 at 76,48) with System.Drawing at 16-256 px and writes a
  PNG-compressed multi-size .ico. Windows only. Run from the repository root:
    pwsh windows/tools/make-app-icon.ps1
#>
param([string]$Out = (Join-Path $PSScriptRoot '..\src\AmbientTheme.App\Assets\app.ico'))

Add-Type -AssemblyName System.Drawing
$ErrorActionPreference = 'Stop'

function New-LogoPng([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.ScaleTransform($size / 120.0, $size / 120.0)

    $amber = [System.Drawing.Color]::FromArgb(0xF5, 0xA5, 0x24)
    $rayBrush = New-Object System.Drawing.SolidBrush $amber
    for ($i = 0; $i -lt 8; $i++) {
        $state = $g.Save()
        $g.TranslateTransform(60, 60); $g.RotateTransform($i * 45); $g.TranslateTransform(-60, -60)
        $path = New-Object System.Drawing.Drawing2D.GraphicsPath
        $path.AddArc(56, 4, 8, 8, 180, 180); $path.AddArc(56, 12, 8, 8, 0, 180); $path.CloseFigure()
        $g.FillPath($rayBrush, $path)
        $g.Restore($state)
    }

    $sunPath = New-Object System.Drawing.Drawing2D.GraphicsPath
    $sunPath.AddEllipse(30, 30, 60, 60)
    $sun = New-Object System.Drawing.Drawing2D.PathGradientBrush $sunPath
    $sun.CenterPoint = New-Object System.Drawing.PointF 51, 49
    $sun.CenterColor = [System.Drawing.Color]::FromArgb(0xFF, 0xD2, 0x7A)
    $sun.SurroundColors = @($amber)
    $g.FillPath($sun, $sunPath)

    $moon = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(0x14, 0x18, 0x21))
    $g.FillEllipse($moon, 52, 24, 48, 48)

    $g.Dispose()
    $stream = New-Object System.IO.MemoryStream
    $bmp.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    return , $stream.ToArray()
}

$sizes = 16, 24, 32, 48, 64, 128, 256
$images = $sizes | ForEach-Object { , (New-LogoPng $_) }

New-Item -ItemType Directory -Force (Split-Path $Out) | Out-Null
$file = [System.IO.File]::Create($Out)
$w = New-Object System.IO.BinaryWriter $file
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]; $dim = if ($s -ge 256) { 0 } else { $s }
    $w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([uint16]1); $w.Write([uint16]32)
    $w.Write([uint32]$images[$i].Length); $w.Write([uint32]$offset)
    $offset += $images[$i].Length
}
foreach ($img in $images) { $w.Write([byte[]]$img) }
$w.Dispose()
"Wrote $Out ($($sizes -join ', ') px)"
