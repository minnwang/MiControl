param(
    [string]$OutFile = "C:\Users\ming_\Documents\deepseek-harness\default-workspace\MiControl\assets\app.ico",
    [string]$PngPreview = "C:\Users\ming_\Documents\deepseek-harness\default-workspace\MiControl\assets\app-preview.png"
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing

function New-MiBitmap([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit
    $g.Clear([System.Drawing.Color]::Transparent)

    # rounded square background, Xiaomi orange
    $radius = [int]($size * 0.22)
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $radius * 2
    $path.AddArc(0, 0, $d, $d, 180, 90)
    $path.AddArc($size - $d, 0, $d, $d, 270, 90)
    $path.AddArc($size - $d, $size - $d, $d, $d, 0, 90)
    $path.AddArc(0, $size - $d, $d, $d, 90, 90)
    $path.CloseFigure()

    $orange = [System.Drawing.Color]::FromArgb(255, 255, 103, 0)
    $brush = New-Object System.Drawing.SolidBrush $orange
    $g.FillPath($brush, $path)
    $brush.Dispose()

    # "mi" wordmark
    $fontSize = [float]($size * 0.46)
    $font = New-Object System.Drawing.Font("Segoe UI", $fontSize, [System.Drawing.FontStyle]::Bold, [System.Drawing.GraphicsUnit]::Pixel)
    $white = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::White)
    $fmt = New-Object System.Drawing.StringFormat
    $fmt.Alignment = [System.Drawing.StringAlignment]::Center
    $fmt.LineAlignment = [System.Drawing.StringAlignment]::Center
    $rect = New-Object System.Drawing.RectangleF 0, 0, $size, $size
    $g.DrawString("mi", $font, $white, $rect, $fmt)

    $white.Dispose(); $font.Dispose(); $fmt.Dispose(); $path.Dispose(); $g.Dispose()
    return $bmp
}

# build ICO with PNG payloads (Vista+ supports PNG inside ICO)
$sizes = 16, 24, 32, 48, 64, 128, 256
$pngs = @()
foreach ($s in $sizes) {
    $bmp = New-MiBitmap $s
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $pngs += ,@($s, $ms.ToArray())
    if ($s -eq 256) { $bmp.Save($PngPreview, [System.Drawing.Imaging.ImageFormat]::Png) }
    $ms.Dispose(); $bmp.Dispose()
}

$fs = [System.IO.File]::Create($OutFile)
$bw = New-Object System.IO.BinaryWriter $fs
$bw.Write([UInt16]0)          # reserved
$bw.Write([UInt16]1)          # type = icon
$bw.Write([UInt16]$pngs.Count)

$offset = 6 + (16 * $pngs.Count)
foreach ($entry in $pngs) {
    $size = $entry[0]
    $data = $entry[1]
    $dim = if ($size -ge 256) { 0 } else { $size }
    $bw.Write([byte]$dim)           # width
    $bw.Write([byte]$dim)           # height
    $bw.Write([byte]0)              # palette
    $bw.Write([byte]0)              # reserved
    $bw.Write([UInt16]1)            # planes
    $bw.Write([UInt16]32)           # bpp
    $bw.Write([UInt32]$data.Length) # bytes
    $bw.Write([UInt32]$offset)      # offset
    $offset += $data.Length
}
foreach ($entry in $pngs) { $bw.Write($entry[1]) }
$bw.Flush(); $bw.Close(); $fs.Close()

Write-Host "written: $OutFile  ($([math]::Round((Get-Item $OutFile).Length/1KB,1)) KB)"
Write-Host "preview: $PngPreview"
