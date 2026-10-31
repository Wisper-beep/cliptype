$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$iconDirectory = Join-Path $PSScriptRoot '..\extension\icons'
New-Item -ItemType Directory -Path $iconDirectory -Force | Out-Null
foreach ($size in @(16, 32, 128)) {
    $bitmap = New-Object System.Drawing.Bitmap($size, $size)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.Clear([System.Drawing.Color]::FromArgb(47, 93, 220))
    $graphics.ScaleTransform(($size / 128.0), ($size / 128.0))
    $pen = New-Object System.Drawing.Pen([System.Drawing.Color]::White, 7)
    $graphics.DrawRectangle($pen, 33, 31, 62, 79)
    $graphics.FillRectangle([System.Drawing.Brushes]::White, 49, 18, 30, 24)
    foreach ($y in @(58, 77, 96)) { $graphics.DrawLine($pen, 46, $y, 82, $y) }
    $bitmap.Save((Join-Path $iconDirectory ($size.ToString() + '.png')), [System.Drawing.Imaging.ImageFormat]::Png)
    $pen.Dispose(); $graphics.Dispose(); $bitmap.Dispose()
}
Write-Output 'Generated extension icons.'
