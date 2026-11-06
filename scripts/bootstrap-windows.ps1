#requires -Version 5.1
[CmdletBinding()]
param(
    [string]$Destination = (Join-Path ([Environment]::GetFolderPath('UserProfile')) 'alycia'),
    [ValidatePattern('^[a-zA-Z0-9][a-zA-Z0-9._:/-]*$')][string]$Model = 'qwen2.5:1.5b'
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
if ([Environment]::OSVersion.Platform -ne 'Win32NT') { throw 'This bootstrap requires Windows.' }
function Invoke-Git {
    param([string[]]$GitArguments, [switch]$Capture)
    if ($Capture) { $result = & git @GitArguments } else { & git @GitArguments | Out-Host }
    if ($LASTEXITCODE -ne 0) { throw "Git failed (exit $LASTEXITCODE)." }
    if ($Capture) { return $result }
}
$env:Path = @([Environment]::GetEnvironmentVariable('Path', 'Machine'),
    [Environment]::GetEnvironmentVariable('Path', 'User'), $env:Path, "$env:ProgramFiles\Git\cmd") -join ';'
if (-not (Get-Command git -ErrorAction SilentlyContinue)) {
    if (-not (Get-Command winget -ErrorAction SilentlyContinue)) {
        throw 'Install/update App Installer (WinGet) from Microsoft Store, then rerun this command.'
    }
    & winget install --id Git.Git --exact --source winget --accept-package-agreements --accept-source-agreements --disable-interactivity
    if ($LASTEXITCODE -ne 0) { throw 'Git installation failed. Complete any requested restart, then rerun.' }
    $env:Path = @([Environment]::GetEnvironmentVariable('Path', 'Machine'),
        [Environment]::GetEnvironmentVariable('Path', 'User'), $env:Path, "$env:ProgramFiles\Git\cmd") -join ';'
}
$branch = 'prototype/conversation-files'
if (-not (Test-Path -LiteralPath $Destination)) {
    Invoke-Git @('clone', '--branch', $branch, '--single-branch', 'https://github.com/alescis-wuin/alycia.git', $Destination)
} else {
    if (-not (Test-Path -LiteralPath (Join-Path $Destination '.git'))) {
        throw "Destination already exists and is not a Git checkout: $Destination. Choose -Destination elsewhere."
    }
    $origin = Invoke-Git @('-C', $Destination, 'remote', 'get-url', 'origin') -Capture
    if ($origin -notin @('https://github.com/alescis-wuin/alycia.git', 'https://github.com/alescis-wuin/alycia', 'git@github.com:alescis-wuin/alycia.git')) {
        throw 'The existing checkout points to another repository. Choose another -Destination.'
    }
    if (Invoke-Git @('-C', $Destination, 'status', '--porcelain') -Capture) {
        throw 'The checkout contains local changes. Commit/stash them or choose another -Destination; nothing was overwritten.'
    }
    Invoke-Git @('-C', $Destination, 'fetch', 'origin', "${branch}:refs/remotes/origin/$branch")
    Invoke-Git @('-C', $Destination, 'switch', $branch)
    Invoke-Git @('-C', $Destination, 'merge', '--ff-only', "origin/$branch")
}
& (Join-Path $Destination 'scripts/demo.ps1') -Action All -Model $Model -InstallPrerequisites
