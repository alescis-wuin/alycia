#requires -Version 5.1
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
# Native programs often write progress to stderr. Check exit codes explicitly.
$PSNativeCommandUseErrorActionPreference = $false
$script:RepoRoot = Split-Path -Parent $PSScriptRoot
$script:ComposeArgs = @('compose', '-f', 'compose.yaml', '-f', 'compose.windows.yaml')

function Invoke-Checked {
    param([string]$Program, [string[]]$Arguments, [switch]$Capture)
    if ($Capture) { $output = & $Program @Arguments } else { & $Program @Arguments | Out-Host }
    if ($LASTEXITCODE -ne 0) { throw "$Program failed (exit $LASTEXITCODE)." }
    if ($Capture) { return $output }
}

function Invoke-Compose {
    param([string[]]$Arguments, [switch]$Capture)
    Invoke-Checked docker ($script:ComposeArgs + $Arguments) -Capture:$Capture
}

function Update-WindowsPath {
    $paths = @([Environment]::GetEnvironmentVariable('Path', 'Machine'),
        [Environment]::GetEnvironmentVariable('Path', 'User'), $env:Path,
        "$env:ProgramFiles\Git\cmd", "$env:ProgramFiles\dotnet",
        "$env:ProgramFiles\Docker\Docker\resources\bin",
        "$env:LOCALAPPDATA\Programs\DockerDesktop\resources\bin")
    $env:Path = ($paths | Where-Object { $_ }) -join ';'
}

function Install-WindowsPackage {
    param([string]$Id)
    if (-not (Get-Command winget -ErrorAction SilentlyContinue)) {
        throw 'WinGet is missing. Install/update App Installer from Microsoft Store, then rerun the command.'
    }
    Invoke-Checked winget @('install', '--id', $Id, '--exact', '--source', 'winget',
        '--accept-package-agreements', '--accept-source-agreements', '--disable-interactivity')
    Update-WindowsPath
}

function Assert-WindowsPrerequisites {
    param([switch]$Install)
    if ([Environment]::OSVersion.Platform -ne 'Win32NT') { throw 'This launcher requires Windows.' }
    if (-not [Environment]::Is64BitOperatingSystem) { throw '64-bit Windows is required.' }
    Update-WindowsPath
    foreach ($item in @(@('git', 'Git.Git'), @('docker', 'Docker.DockerDesktop'))) {
        if (-not (Get-Command $item[0] -ErrorAction SilentlyContinue)) {
            if (-not $Install) { throw "Missing $($item[0]). Run bootstrap-windows.ps1 or demo.ps1 -InstallPrerequisites." }
            Install-WindowsPackage $item[1]
        }
    }
    $hasSdk = $false
    if (Get-Command dotnet -ErrorAction SilentlyContinue) {
        $hasSdk = @((Invoke-Checked dotnet @('--list-sdks') -Capture) | Where-Object { $_ -match '^10\.' }).Count -gt 0
    }
    if (-not $hasSdk) {
        if (-not $Install) { throw '.NET SDK 10 is missing. Run demo.ps1 -InstallPrerequisites.' }
        Install-WindowsPackage 'Microsoft.DotNet.SDK.10'
    }
    if (@((Invoke-Checked dotnet @('--list-sdks') -Capture) | Where-Object { $_ -match '^10\.' }).Count -eq 0) {
        throw '.NET SDK 10 is still unavailable. Open a new terminal and rerun the command.'
    }
    Invoke-Checked docker @('compose', 'version')
}

function Get-DockerEngineType {
    # PowerShell 5.1 can turn redirected native stderr into terminating errors.
    $saved = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        $result = & docker info --format '{{.OSType}}' 2>$null
        if ($LASTEXITCODE -eq 0) { return "$result".Trim() }
        return ''
    } finally { $ErrorActionPreference = $saved }
}

function Start-WindowsDocker {
    $kind = Get-DockerEngineType
    if ($kind -eq 'linux') { return }
    if ($kind -eq 'windows') { throw 'Switch Docker Desktop to Linux containers, then rerun the command.' }
    $desktop = @("$env:ProgramFiles\Docker\Docker\Docker Desktop.exe",
        "$env:LOCALAPPDATA\Programs\DockerDesktop\Docker Desktop.exe") |
        Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
    if (-not $desktop) { throw 'Docker Desktop was not found. Install/start Docker Desktop with its WSL 2 backend.' }
    Start-Process -FilePath $desktop
    Write-Host 'Waiting for Docker Desktop (up to 3 minutes). Complete any first-run dialog.'
    $deadline = (Get-Date).AddMinutes(3)
    do {
        Start-Sleep -Seconds 3
        $kind = Get-DockerEngineType
        if ($kind -eq 'linux') { return }
        if ($kind -eq 'windows') { throw 'Switch Docker Desktop to Linux containers.' }
    } while ((Get-Date) -lt $deadline)
    throw 'Docker is not ready. Enable/update WSL 2 and virtualization; restart Windows if requested, then rerun the same command. No volumes were deleted.'
}

function Initialize-Wsl {
    if ((Get-DockerEngineType) -eq 'linux') { return }
    $saved = $ErrorActionPreference
    $available = $false
    try {
        $ErrorActionPreference = 'Continue'
        if (Get-Command wsl.exe -ErrorAction SilentlyContinue) {
            & wsl.exe --version 2>$null | Out-Null
            $available = $LASTEXITCODE -eq 0
        }
    } finally { $ErrorActionPreference = $saved }
    if ($available) { return }
    Write-Host 'WSL 2 is missing or too old. Windows will request administrator permission to install it.'
    $process = Start-Process powershell.exe -Verb RunAs -Wait -PassThru -ArgumentList @(
        '-NoProfile', '-ExecutionPolicy', 'Bypass', '-Command',
        '"& wsl.exe --install --no-distribution; exit $LASTEXITCODE"')
    if ($process.ExitCode -notin @(0, 3010)) { throw 'WSL installation failed. Check Windows virtualization support and install/update WSL 2.' }
    throw 'WSL installation was requested. Restart Windows, then rerun the same bootstrap command to continue. Existing data was preserved.'
}

function New-LocalSecret {
    $bytes = New-Object byte[] 32
    $rng = [Security.Cryptography.RandomNumberGenerator]::Create()
    try { $rng.GetBytes($bytes) } finally { $rng.Dispose() }
    return [BitConverter]::ToString($bytes).Replace('-', '').ToLowerInvariant()
}

function Initialize-DemoEnvironment {
    $path = Join-Path $script:RepoRoot '.env'
    $utf8 = New-Object Text.UTF8Encoding($false)
    if (-not (Test-Path -LiteralPath $path)) {
        # CreateNew never replaces an existing password, even on concurrent launch.
        $stream = [IO.File]::Open($path, [IO.FileMode]::CreateNew)
        try {
            $bytes = $utf8.GetBytes("POSTGRES_PASSWORD=$(New-LocalSecret)`n")
            $stream.Write($bytes, 0, $bytes.Length)
        } finally { $stream.Dispose() }
    }
    $text = [IO.File]::ReadAllText($path)
    if ($text -match '(?m)^POSTGRES_PASSWORD=replace-with-a-strong-random-secret\s*$') {
        throw 'Replace the example POSTGRES_PASSWORD in .env with the existing database password or a new strong secret.'
    }
    if ($text -notmatch '(?m)^ALYCIA_CHAT_ACCESS_TOKEN=') {
        [IO.File]::AppendAllText($path, "`nALYCIA_CHAT_ACCESS_TOKEN=$(New-LocalSecret)`n", $utf8)
    }
    $config = (Invoke-Compose @('config', '--format', 'json') -Capture) -join "`n" | ConvertFrom-Json
    $token = $config.services.'chat-api'.environment.ALYCIA_CHAT_ACCESS_TOKEN
    if ($token -notmatch '\A[0-9a-fA-F]{64}\z') { throw 'ALYCIA_CHAT_ACCESS_TOKEN in .env must contain 64 random hex characters.' }
    $pg = $config.services.postgres.environment
    $script:PostgresEnvironment = $pg
    $connection = New-Object System.Data.Common.DbConnectionStringBuilder
    $connection['Host'] = 'postgres'
    $connection['Database'] = [string]$pg.POSTGRES_DB
    $connection['Username'] = [string]$pg.POSTGRES_USER
    $connection['Password'] = [string]$pg.POSTGRES_PASSWORD
    $connection['Timeout'] = 15
    $connection['Command Timeout'] = 15
    $env:ALYCIA_CHAT_CONNECTION = $connection.get_ConnectionString()
    $env:ALYCIA_CHAT_ACCESS_TOKEN = $token
    $env:ALYCIA_API_BASE_URL = 'http://127.0.0.1:5187'
}

function Wait-ChatApi {
    $headers = @{ Authorization = "Bearer $env:ALYCIA_CHAT_ACCESS_TOKEN" }
    $deadline = (Get-Date).AddSeconds(90)
    do {
        try {
            # Verify database access and the client token, not only /healthz.
            $null = Invoke-RestMethod "$env:ALYCIA_API_BASE_URL/api/chat/conversations" -Headers $headers -TimeoutSec 5
            $models = @(Invoke-RestMethod "$env:ALYCIA_API_BASE_URL/api/chat/models" -Headers $headers -TimeoutSec 10)
            if ($models.Count -gt 0) { return }
        } catch { }
        Start-Sleep -Seconds 2
    } while ((Get-Date) -lt $deadline)
    throw 'Chat API is not ready. Check docker compose -f compose.yaml -f compose.windows.yaml logs --tail 60 chat-api ollama. Port 5187 may be occupied by the previous demo.'
}
