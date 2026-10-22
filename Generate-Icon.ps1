param()
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$assetFolder = Join-Path $PSScriptRoot 'assets'
New-Item -ItemType Directory -Path $assetFolder -Force | Out-Null
$frames = @()
foreach ($dimension in 16, 32, 48, 256) {
    $bitmap = New-Object System.Drawing.Bitmap($dimension, $dimension)
    $drawing = [System.Drawing.Graphics]::FromImage($bitmap)
    $drawing.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $drawing.Clear([System.Drawing.Color]::Transparent)
    $scale = $dimension / 64.0
    $drawing.ScaleTransform($scale, $scale)
    $white = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(250,250,250))
    $pen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(32,32,32), 2.7)
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $points = [System.Drawing.PointF[]]@([System.Drawing.PointF]::new(14,5),[System.Drawing.PointF]::new(40,5),[System.Drawing.PointF]::new(51,16),[System.Drawing.PointF]::new(51,58),[System.Drawing.PointF]::new(14,58))
    $path.AddPolygon($points)
    $drawing.FillPath($white,$path)
    $drawing.DrawPath($pen,$path)
    $drawing.DrawLines($pen,[System.Drawing.PointF[]]@([System.Drawing.PointF]::new(39,5),[System.Drawing.PointF]::new(39,17),[System.Drawing.PointF]::new(51,17)))
    $pen.Width = 3.7
    $drawing.DrawLine($pen,27,27,23,48)
    $drawing.DrawLine($pen,39,27,35,48)
    $drawing.DrawLine($pen,19,33,44,33)
    $drawing.DrawLine($pen,18,41,43,41)
    $stream = New-Object System.IO.MemoryStream
    $bitmap.Save($stream,[System.Drawing.Imaging.ImageFormat]::Png)
    $frames += ,$stream.ToArray()
    if ($dimension -eq 256) { $bitmap.Save((Join-Path $assetFolder 'app-icon.png'),[System.Drawing.Imaging.ImageFormat]::Png) }
    $stream.Dispose(); $path.Dispose(); $pen.Dispose(); $white.Dispose(); $drawing.Dispose(); $bitmap.Dispose()
}
$icoPath = Join-Path $assetFolder 'app.ico'
$iconStream = [System.IO.File]::Create($icoPath)
$writer = New-Object System.IO.BinaryWriter($iconStream)
try {
    $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]4)
    $offset = 6 + 16 * 4
    $sizes = @(16,32,48,256)
    for ($index = 0; $index -lt 4; $index++) {
        $sizeByte = if ($sizes[$index] -eq 256) { 0 } else { $sizes[$index] }
        $writer.Write([byte]$sizeByte); $writer.Write([byte]$sizeByte)
        $writer.Write([byte]0); $writer.Write([byte]0)
        $writer.Write([uint16]1); $writer.Write([uint16]32)
        $writer.Write([uint32]$frames[$index].Length); $writer.Write([uint32]$offset)
        $offset += $frames[$index].Length
    }
    foreach ($frame in $frames) { $writer.Write([byte[]]$frame) }
} finally { $writer.Dispose(); $iconStream.Dispose() }
Write-Output "Generated assets/app.ico and assets/app-icon.png"
