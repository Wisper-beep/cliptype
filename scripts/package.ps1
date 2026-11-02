param([switch]$SkipBuild)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$output = Join-Path $projectRoot 'dist'
if (-not $SkipBuild) { & (Join-Path $projectRoot 'build.ps1') -Test }
New-Item -ItemType Directory -Path $output -Force | Out-Null
Push-Location -LiteralPath $projectRoot
try {
    python (Join-Path $PSScriptRoot 'build_extensions.py')
    if ($LASTEXITCODE -ne 0) { throw 'Extension build failed.' }
    python (Join-Path $PSScriptRoot 'package_linux.py')
    if ($LASTEXITCODE -ne 0) { throw 'Linux prototype packaging failed.' }
} finally { Pop-Location }
$windowsItems = @((Join-Path $output 'ClipType.exe'), (Join-Path $output 'README.md'), (Join-Path $projectRoot 'LICENSE'), (Join-Path $projectRoot 'CHANGELOG.md'))
Compress-Archive -LiteralPath $windowsItems -DestinationPath (Join-Path $output 'ClipType-Windows.zip') -Force
$artifacts = @('ClipType-Windows.zip','ClipType-Chromium.zip','ClipType-Firefox.zip','ClipType-Linux.tar.gz','cliptype.pyz','ClipType-Extension.zip') | ForEach-Object { Join-Path $output $_ }
Get-FileHash -LiteralPath $artifacts -Algorithm SHA256 | ForEach-Object { $_.Hash.ToLower() + '  ' + (Split-Path -Leaf $_.Path) } | Set-Content -LiteralPath (Join-Path $output 'SHA256SUMS.txt') -Encoding ascii
Write-Output 'Packaged Windows, Chromium, Firefox development build and Linux prototype into dist.'
