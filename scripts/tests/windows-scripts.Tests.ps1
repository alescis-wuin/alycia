#requires -Version 5.1
# Runs without a Docker daemon. A standalone Compose binary is supported for CI.
[CmdletBinding()]
param([string]$ComposeExecutable = 'docker', [switch]$StandaloneCompose)
. (Join-Path (Split-Path -Parent $PSScriptRoot) 'windows-common.ps1')
$sourceRoot = $script:RepoRoot
$temporary = Join-Path ([IO.Path]::GetTempPath()) ('alycia scripts ' + [Guid]::NewGuid().ToString('N'))
$environment = @{}
foreach ($key in @('ALYCIA_CHAT_CONNECTION', 'ALYCIA_CHAT_ACCESS_TOKEN', 'ALYCIA_API_BASE_URL', 'POSTGRES_PASSWORD')) {
    $environment[$key] = [Environment]::GetEnvironmentVariable($key)
    Remove-Item "Env:$key" -ErrorAction SilentlyContinue
}
function Assert-True {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) { throw $Message }
}
function Invoke-Compose {
    param([string[]]$Arguments, [switch]$Capture)
    $cli = @('-f', 'compose.yaml', '-f', 'compose.windows.yaml') + $Arguments
    if (-not $StandaloneCompose) { $cli = @('compose') + $cli }
    Invoke-Checked $ComposeExecutable $cli -Capture:$Capture
}
try {
    $null = New-Item -ItemType Directory -Path $temporary
    Copy-Item (Join-Path $sourceRoot 'compose.yaml'), (Join-Path $sourceRoot 'compose.windows.yaml') $temporary
    $script:RepoRoot = $temporary
    Push-Location $temporary
    try {
        Initialize-DemoEnvironment
        $original = [IO.File]::ReadAllText((Join-Path $temporary '.env'))
        Assert-True ($original -match 'POSTGRES_PASSWORD=[0-9a-f]{64}') 'Random password was not generated.'
        Assert-True ($env:ALYCIA_CHAT_ACCESS_TOKEN -match '\A[0-9a-f]{64}\z') 'Random client token was not generated.'
        $token = $env:ALYCIA_CHAT_ACCESS_TOKEN
        Initialize-DemoEnvironment
        Assert-True ($original -ceq [IO.File]::ReadAllText((Join-Path $temporary '.env'))) 'An existing .env was modified.'
        Assert-True ($token -ceq $env:ALYCIA_CHAT_ACCESS_TOKEN) 'Token changed on second launch.'
        Assert-True ((New-LocalSecret) -cne (New-LocalSecret)) 'Independent random secrets collided.'

        # Preserve an existing password containing connection-string delimiters.
        $env:ALYCIA_CHAT_ACCESS_TOKEN = $null
        $env:ALYCIA_CHAT_CONNECTION = $null
        [IO.File]::WriteAllText((Join-Path $temporary '.env'), "POSTGRES_PASSWORD='keep;this=secret value'`n", (New-Object Text.UTF8Encoding($false)))
        Initialize-DemoEnvironment
        $parsed = New-Object System.Data.Common.DbConnectionStringBuilder
        $parsed.set_ConnectionString($env:ALYCIA_CHAT_CONNECTION)
        Assert-True ($parsed['Password'] -ceq 'keep;this=secret value') 'Connection-string escaping lost password characters.'
        Assert-True ($parsed['Host'] -eq 'postgres') 'Windows API is not using the Compose database service.'
        $config = (Invoke-Compose @('config', '--format', 'json') -Capture) -join "`n" | ConvertFrom-Json
        Assert-True ($config.services.'chat-api'.ports[0].host_ip -eq '127.0.0.1') 'API port is not loopback-only.'
        Assert-True ($config.services.'chat-api'.environment.ALYCIA_CHAT_CONTAINER_MODE -eq 'true') 'Authenticated bridge mode is disabled.'
        Assert-True ($null -eq $config.services.postgres.PSObject.Properties['ports']) 'PostgreSQL is exposed on Windows.'
        Assert-True ($null -eq $config.services.ollama.PSObject.Properties['ports']) 'Ollama is exposed on Windows.'

        $env:ALYCIA_CHAT_ACCESS_TOKEN = 'weak'
        $rejected = $false
        try { Initialize-DemoEnvironment } catch { $rejected = $_.Exception.Message -like '*64 random hex*' }
        Assert-True $rejected 'Weak or overridden token was accepted.'
        $env:ALYCIA_CHAT_ACCESS_TOKEN = $null
        [IO.File]::WriteAllText((Join-Path $temporary '.env'), 'POSTGRES_PASSWORD=replace-with-a-strong-random-secret')
        $rejected = $false
        try { Initialize-DemoEnvironment } catch { $rejected = $_.Exception.Message -like '*example POSTGRES_PASSWORD*' }
        Assert-True $rejected 'Example password was accepted.'

        function Native-Failure { $global:LASTEXITCODE = 7 }
        $rejected = $false
        try { Invoke-Checked Native-Failure @() } catch { $rejected = $_.Exception.Message -like '*exit 7*' }
        Assert-True $rejected 'Native command failure was swallowed.'
        Write-Host 'PASS: environment creation/reuse, random secrets, password quoting, Compose exposure, token rejection, native exit codes and paths containing spaces.'
    } finally { Pop-Location }
} finally {
    $script:RepoRoot = $sourceRoot
    foreach ($key in $environment.Keys) {
        if ($null -eq $environment[$key]) { Remove-Item "Env:$key" -ErrorAction SilentlyContinue }
        else { [Environment]::SetEnvironmentVariable($key, $environment[$key]) }
    }
    if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary -Recurse -Force }
}
