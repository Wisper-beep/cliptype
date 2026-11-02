param([switch]$Test)
$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { throw '.NET Framework C# compiler was not found.' }
$output = Join-Path $projectRoot 'dist'
New-Item -ItemType Directory -Path $output -Force | Out-Null
$sources = @(Get-ChildItem -LiteralPath (Join-Path $projectRoot 'src') -Filter '*.cs' | ForEach-Object { $_.FullName })
$references = @('/r:System.dll', '/r:System.Core.dll', '/r:System.Drawing.dll', '/r:System.Windows.Forms.dll', '/r:System.Xml.dll')
& $compiler /nologo /utf8output /codepage:65001 /optimize+ /target:winexe /platform:anycpu "/win32manifest:$projectRoot\app.manifest" "/out:$output\ClipType.exe" @references @sources
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
Copy-Item -LiteralPath (Join-Path $projectRoot 'docs\windows.md') -Destination (Join-Path $output 'README.md') -Force
Write-Output "Built: $output\ClipType.exe"
if ($Test) {
    $testSources = @($sources) + @((Join-Path $projectRoot 'tests\Tests.cs'))
    & $compiler /nologo /utf8output /codepage:65001 /target:exe /main:Tests "/out:$output\ClipType.Tests.exe" @references @testSources
    if ($LASTEXITCODE -ne 0) { throw 'Test compilation failed.' }
    & $compiler /nologo /utf8output /codepage:65001 /target:winexe "/out:$output\ClipType.Target.exe" @references (Join-Path $projectRoot 'tests\TargetFixture.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Fixture compilation failed.' }
    & (Join-Path $output 'ClipType.Tests.exe')
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
}
