param(
    [ValidateSet('Inspect', 'Create', 'Release', 'Status')][string]$Action = 'Inspect',
    [string]$Name = 'cliptype',
    [switch]$Private,
    [string]$Tag = 'v1.1.0-rc.1',
    [switch]$Prerelease,
    [string]$TargetCommitish = 'vnext',
    [string]$ExpectedHead = '',
    [string]$NotesPath = ''
)
$ErrorActionPreference = 'Stop'
if ($Name -notmatch '^[a-zA-Z0-9._-]+$' -or $Tag -notmatch '^v[0-9]+\.[0-9]+\.[0-9]+(?:-[A-Za-z0-9.-]+)?$') { throw 'Invalid repository name or version.' }
if ($Action -eq 'Release' -and $Tag.Contains('-') -and -not $Prerelease) { throw 'A preview tag requires -Prerelease.' }
if ($ExpectedHead -and $ExpectedHead -notmatch '^[a-f0-9]{40}$') { throw 'ExpectedHead must be a full commit SHA.' }
$env:GIT_TERMINAL_PROMPT = '0'
$env:GCM_INTERACTIVE = 'Never'
$credentialLines = "protocol=https`nhost=github.com`nusername=Wisper-beep`n`n" | git credential fill
if ($LASTEXITCODE -ne 0) { throw 'No existing GitHub credential is available.' }
$credential = @{}
foreach ($line in $credentialLines) {
    $parts = $line -split '=', 2
    if ($parts.Length -eq 2) { $credential[$parts[0]] = $parts[1] }
}
if (-not $credential['password']) { throw 'GitHub credential is missing.' }
$headers = @{ Authorization = 'Bearer ' + $credential['password']; Accept = 'application/vnd.github+json'; 'X-GitHub-Api-Version' = '2022-11-28'; 'User-Agent' = 'ClipType-Publish' }
$credential.Clear(); $credentialLines = $null
function Invoke-GitHub([string]$Method, [string]$Path, $Body = $null) {
    $arguments = @{ Uri = 'https://api.github.com' + $Path; Headers = $headers; Method = $Method }
    if ($null -ne $Body) { $arguments.Body = [System.Text.Encoding]::UTF8.GetBytes(($Body | ConvertTo-Json -Depth 8)); $arguments.ContentType = 'application/json; charset=utf-8' }
    try { Invoke-RestMethod @arguments }
    catch { throw ('GitHub API request failed: ' + $Method + ' ' + $Path + ' (HTTP ' + [int]$_.Exception.Response.StatusCode + ').') }
}
$user = Invoke-GitHub 'Get' '/user'
if ($user.login -ne 'Wisper-beep') { throw 'Unexpected authenticated account; refusing to publish.' }
$repoPath = '/repos/' + $user.login + '/' + $Name
if ($Action -eq 'Inspect') {
    Write-Output ('Authenticated: ' + $user.login)
    try { $repo = Invoke-GitHub 'Get' $repoPath; Write-Output ('Repository exists: ' + $repo.html_url) }
    catch { if ($_.Exception.Message -match 'HTTP 404') { Write-Output 'Repository does not exist.' } else { throw } }
} elseif ($Action -eq 'Status') {
    $repo = Invoke-GitHub 'Get' $repoPath
    Write-Output ('Repository: ' + $repo.html_url + ' (' + $repo.visibility + ')')
    $runs = Invoke-GitHub 'Get' ($repoPath + '/actions/runs?per_page=5')
    foreach ($run in $runs.workflow_runs) { Write-Output ($run.head_sha + ' | ' + $run.status + ' | ' + $run.conclusion + ' | ' + $run.html_url) }
} elseif ($Action -eq 'Create') {
    $repo = Invoke-GitHub 'Post' '/user/repos' @{name = $Name; description = 'Clipboard to caret: a Windows typing utility and Chrome / Edge extension.'; private = [bool]$Private; auto_init = $false; has_issues = $true; has_wiki = $false}
    Write-Output ('Created: ' + $repo.html_url)
    Write-Output ('Visibility: ' + $repo.visibility)
} else {
    $assetNames = @('ClipType-Windows.zip','ClipType-Chromium.zip','ClipType-Firefox.zip','ClipType-Linux.tar.gz','cliptype.pyz','ClipType-Extension.zip','SHA256SUMS.txt')
    foreach ($assetName in $assetNames) {
        if (-not (Test-Path -LiteralPath (Join-Path $PSScriptRoot ('..\dist\' + $assetName)))) { throw ('Release asset missing: ' + $assetName) }
    }
    if ($ExpectedHead) {
        $remoteCommit = Invoke-GitHub 'Get' ($repoPath + '/commits/' + [Uri]::EscapeDataString($TargetCommitish))
        if ($remoteCommit.sha -ne $ExpectedHead) { throw 'Remote target changed; refusing to publish mismatched artifacts.' }
    }
    if (-not $NotesPath) { $NotesPath = Join-Path $PSScriptRoot '..\RELEASE_NOTES.md' }
    $notes = Get-Content -LiteralPath $NotesPath -Raw -Encoding UTF8
    $release = Invoke-GitHub 'Post' ($repoPath + '/releases') @{tag_name = $Tag; target_commitish = $TargetCommitish; name = 'ClipType ' + $Tag; body = $notes; draft = $false; prerelease = [bool]$Prerelease; make_latest = 'false'}
    foreach ($assetName in $assetNames) {
        $assetPath = Join-Path $PSScriptRoot ('..\dist\' + $assetName)
        if (-not (Test-Path -LiteralPath $assetPath)) { throw ('Release asset missing: ' + $assetName) }
        $uri = ($release.upload_url -replace '\{.*$', '') + '?name=' + $assetName
        $contentType = if ($assetName.EndsWith('.zip')) { 'application/zip' } elseif ($assetName.EndsWith('.txt')) { 'text/plain' } else { 'application/octet-stream' }
        try { $asset = Invoke-RestMethod -Uri $uri -Headers $headers -Method Post -ContentType $contentType -InFile $assetPath }
        catch { throw ('GitHub release upload failed for ' + $assetName + '. Release: ' + $release.html_url) }
        Write-Output ('Uploaded: ' + $asset.browser_download_url)
    }
    Write-Output ('Release: ' + $release.html_url)
}
$headers.Clear()
