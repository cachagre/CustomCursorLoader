param()
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$project = Split-Path $PSScriptRoot -Parent
$target = Join-Path $project 'assets\default.png'
New-Item -ItemType Directory -Path (Split-Path $target -Parent) -Force | Out-Null
$bitmap = [Drawing.Bitmap]::new(32, 32)
$graphics = [Drawing.Graphics]::FromImage($bitmap)
$pen = [Drawing.Pen]::new([Drawing.Color]::FromArgb(255, 22, 37, 52), 2)
$brush = [Drawing.SolidBrush]::new([Drawing.Color]::FromArgb(255, 86, 225, 191))
try {
    $graphics.Clear([Drawing.Color]::Transparent)
    $graphics.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $points = [Drawing.PointF[]]@(
        [Drawing.PointF]::new(3, 3), [Drawing.PointF]::new(3, 25),
        [Drawing.PointF]::new(9, 19), [Drawing.PointF]::new(14, 29),
        [Drawing.PointF]::new(18, 27), [Drawing.PointF]::new(13, 17),
        [Drawing.PointF]::new(23, 17)
    )
    $graphics.FillPolygon($brush, $points)
    $graphics.DrawPolygon($pen, $points)
    $bitmap.Save($target, [Drawing.Imaging.ImageFormat]::Png)
    Write-Host "已生成原创几何光标：$target"
}
finally { $graphics.Dispose(); $pen.Dispose(); $brush.Dispose(); $bitmap.Dispose() }
