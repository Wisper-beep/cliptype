param(
    [ValidateSet('Inspect', 'Create', 'Release', 'Status')][string]$Action = 'Inspect',
    [string]$Name = 'cliptype',
    [switch]$Private,
    [string]$Tag = 'v1.0.0'
)
$ErrorActionPreference = 'Stop'
if ($Name -notmatch '^[a-zA-Z0-9._-]+$' -or $Tag -notmatch '^v[0-9]+\.[0-9]+\.[0-9]+$') { throw 'Invalid repository name or version.' }
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
    $notes = Get-Content -LiteralPath (Join-Path $PSScriptRoot '..\RELEASE_NOTES.md') -Raw -Encoding UTF8
    $release = Invoke-GitHub 'Post' ($repoPath + '/releases') @{tag_name = $Tag; name = 'ClipType ' + $Tag; body = $notes; draft = $false; prerelease = $false}
    foreach ($assetName in @('ClipType-Windows.zip', 'ClipType-Extension.zip', 'SHA256SUMS.txt')) {
        $assetPath = Join-Path $PSScriptRoot ('..\dist\' + $assetName)
        if (-not (Test-Path -LiteralPath $assetPath)) { throw ('Release asset missing: ' + $assetName) }
        $uri = ($release.upload_url -replace '\{.*$', '') + '?name=' + $assetName
        $contentType = if ($assetName.EndsWith('.zip')) { 'application/zip' } else { 'text/plain' }
        try { $asset = Invoke-RestMethod -Uri $uri -Headers $headers -Method Post -ContentType $contentType -InFile $assetPath }
        catch { throw ('GitHub release upload failed for ' + $assetName + '. Release: ' + $release.html_url) }
        Write-Output ('Uploaded: ' + $asset.browser_download_url)
    }
    Write-Output ('Release: ' + $release.html_url)
}
$headers.Clear()
