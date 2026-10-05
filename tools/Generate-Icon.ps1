# Regenerates NetParity\Assets\NetParity.ico.
#
# The icon is committed to the repository because the build needs it (it is referenced
# by ApplicationIcon in NetParity.csproj), and a build that depends on an image editor is
# a build that breaks on someone else's machine. Re-run this only when the artwork changes:
#
#   powershell -ExecutionPolicy Bypass -File tools\Generate-Icon.ps1

[CmdletBinding()]
param(
    [string]$OutputPath
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

if (-not $OutputPath) {
    $toolsDir = Split-Path -Parent (Resolve-Path $PSCommandPath)
    $repoRoot = Split-Path -Parent $toolsDir
    $OutputPath = Join-Path (Join-Path (Join-Path $repoRoot 'NetParity') 'Assets') 'NetParity.ico'
}

$Accent = [System.Drawing.Color]::FromArgb(0x00, 0xF2, 0xFF)
$Secondary = [System.Drawing.Color]::FromArgb(0xFF, 0x00, 0xE5)
$Background = [System.Drawing.Color]::FromArgb(0x1A, 0x1A, 0x1A)

function New-RoundedRectanglePath {
    param(
        [float]$X,
        [float]$Y,
        [float]$Width,
        [float]$Height,
        [float]$Radius
    )

    $path = [System.Drawing.Drawing2D.GraphicsPath]::new()
    $diameter = $Radius * 2

    $path.AddArc($X, $Y, $diameter, $diameter, 180, 90)
    $path.AddArc($X + $Width - $diameter, $Y, $diameter, $diameter, 270, 90)
    $path.AddArc($X + $Width - $diameter, $Y + $Height - $diameter, $diameter, $diameter, 0, 90)
    $path.AddArc($X, $Y + $Height - $diameter, $diameter, $diameter, 90, 90)
    $path.CloseFigure()

    return $path
}

function New-IconPng {
    param([int]$Size)

    $bitmap = [System.Drawing.Bitmap]::new($Size, $Size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)

    try {
        $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $graphics.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit
        $graphics.Clear([System.Drawing.Color]::Transparent)

        $inset = [Math]::Max(1.0, $Size * 0.06)
        $side = $Size - (2 * $inset)
        $radius = $side * 0.24

        $body = New-RoundedRectanglePath -X $inset -Y $inset -Width $side -Height $side -Radius $radius
        $brush = [System.Drawing.SolidBrush]::new($Background)
        $graphics.FillPath($brush, $body)

        $edge = [System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(0x33, 0xFF, 0xFF, 0xFF), [Math]::Max(1.0, $Size * 0.03))
        $graphics.DrawPath($edge, $body)

        # Two chevrons read as throughput direction far better than a letterform does
        # once the icon is down to 16 pixels in the taskbar.
        $thick = [Math]::Max(1.5, $side * 0.13)
        $width = $side * 0.34
        $x = $Size / 2

        $downBrush = [System.Drawing.SolidBrush]::new($Accent)
        $upBrush = [System.Drawing.SolidBrush]::new($Secondary)

        $down = [System.Drawing.PointF[]]@(
            [System.Drawing.PointF]::new($x - $width, $side * 0.22),
            [System.Drawing.PointF]::new($x + $width, $side * 0.22),
            [System.Drawing.PointF]::new($x, $side * 0.60)
        )
        $graphics.FillPolygon($downBrush, $down)

        $up = [System.Drawing.PointF[]]@(
            [System.Drawing.PointF]::new($x - $width, $side * 0.78),
            [System.Drawing.PointF]::new($x + $width, $side * 0.78),
            [System.Drawing.PointF]::new($x, $side * 0.40)
        )
        $graphics.FillPolygon($upBrush, $up)

        # Punch a thin gap between the two arrows so they stay distinct at 16px.
        $gap = [System.Drawing.SolidBrush]::new($Background)
        $graphics.FillRectangle($gap, ($Size * 0.06), ($side * 0.685), ($side * 0.88), [Math]::Max(1.0, $thick * 0.35))

        $stream = [System.IO.MemoryStream]::new()
        $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)

        # The comma keeps PowerShell from enumerating the byte array into the pipeline.
        return ,$stream.ToArray()
    }
    finally {
        $graphics.Dispose()
        $bitmap.Dispose()
    }
}

# ICONDIR: reserved, type=1 (icon), image count.
$sizes = @(16, 24, 32, 48, 64, 128, 256)
$images = [System.Collections.Generic.List[byte[]]]::new()
foreach ($size in $sizes) {
    $images.Add((New-IconPng -Size $size))
}

$directory = [System.IO.MemoryStream]::new()
$writer = [System.IO.BinaryWriter]::new($directory)

$writer.Write([UInt16]0)              # reserved
$writer.Write([UInt16]1)              # type: icon
$writer.Write([UInt16]$images.Count)

$offset = 6 + (16 * $images.Count)

for ($i = 0; $i -lt $images.Count; $i++) {
    $size = $sizes[$i]
    $payload = $images[$i]

    # A dimension byte of 0 encodes 256, the format's maximum.
    $writer.Write([Byte]$(if ($size -ge 256) { 0 } else { $size }))
    $writer.Write([Byte]$(if ($size -ge 256) { 0 } else { $size }))
    $writer.Write([Byte]0)            # palette size
    $writer.Write([Byte]0)            # reserved
    $writer.Write([UInt16]1)          # colour planes
    $writer.Write([UInt16]32)         # bits per pixel
    $writer.Write([UInt32]$payload.Length)
    $writer.Write([UInt32]$offset)

    $offset += $payload.Length
}

$writer.Flush()

foreach ($payload in $images) {
    $writer.Write($payload, 0, $payload.Length)
}

$writer.Flush()
$writer.Dispose()

$directoryName = Split-Path -Parent $OutputPath
if (-not (Test-Path -LiteralPath $directoryName)) {
    New-Item -ItemType Directory -Path $directoryName -Force | Out-Null
}

[System.IO.File]::WriteAllBytes($OutputPath, $directory.ToArray())
$directory.Dispose()

Write-Host "Wrote $OutputPath ($((Get-Item -LiteralPath $OutputPath).Length) bytes, $($images.Count) sizes)"
