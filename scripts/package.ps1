param([switch]$SkipBuild)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$output = Join-Path $projectRoot 'dist'
if (-not $SkipBuild) { & (Join-Path $projectRoot 'build.ps1') -Test }
New-Item -ItemType Directory -Path $output -Force | Out-Null
$extension = Join-Path $projectRoot 'extension'
Copy-Item -LiteralPath (Join-Path $projectRoot 'LICENSE') -Destination (Join-Path $extension 'LICENSE') -Force
$extensionItems = @(Get-ChildItem -LiteralPath $extension | ForEach-Object { $_.FullName })
Compress-Archive -LiteralPath $extensionItems -DestinationPath (Join-Path $output 'ClipType-Extension.zip') -Force
$windowsItems = @((Join-Path $output 'ClipType.exe'), (Join-Path $output 'README.md'), (Join-Path $projectRoot 'LICENSE'))
Compress-Archive -LiteralPath $windowsItems -DestinationPath (Join-Path $output 'ClipType-Windows.zip') -Force
Get-FileHash -LiteralPath (Join-Path $output 'ClipType-Extension.zip'), (Join-Path $output 'ClipType-Windows.zip') -Algorithm SHA256 | ForEach-Object { $_.Hash.ToLower() + '  ' + (Split-Path -Leaf $_.Path) } | Set-Content -LiteralPath (Join-Path $output 'SHA256SUMS.txt') -Encoding ascii
Write-Output 'Packaged desktop and extension releases into dist.'
