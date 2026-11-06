#requires -Version 5.1
[CmdletBinding()]
param(
    [ValidateSet('All', 'Api', 'Client', 'Stop', 'Status')][string]$Action = 'All',
    [ValidatePattern('^[a-zA-Z0-9][a-zA-Z0-9._:/-]*$')][string]$Model = 'qwen2.5:1.5b',
    [switch]$InstallPrerequisites
)
. (Join-Path $PSScriptRoot 'windows-common.ps1')
Push-Location $script:RepoRoot
try {
    Assert-WindowsPrerequisites -Install:$InstallPrerequisites
    if ($Action -eq 'Stop') {
        Invoke-Compose @('stop', 'chat-api', 'ollama', 'postgres')
        return
    }
    if ($Action -eq 'Status') { Invoke-Compose @('ps'); return }
    if ($InstallPrerequisites) { Initialize-Wsl }
    Start-WindowsDocker
    Initialize-DemoEnvironment
    if ($Action -in @('All', 'Api')) {
        Invoke-Compose @('up', '-d', '--wait', 'postgres', 'ollama')
        $previousPassword = $env:PGPASSWORD
        try {
            $env:PGPASSWORD = $script:PostgresEnvironment.POSTGRES_PASSWORD
            # Pass the environment variable name only, never its value on the command line.
            Invoke-Compose @('exec', '-T', '-e', 'PGPASSWORD', 'postgres', 'psql', '-X', '-h', '127.0.0.1',
                '-U', $script:PostgresEnvironment.POSTGRES_USER, '-d', $script:PostgresEnvironment.POSTGRES_DB,
                '-v', 'ON_ERROR_STOP=1', '-c', 'SELECT 1')
        } finally { $env:PGPASSWORD = $previousPassword }
        & (Join-Path $PSScriptRoot 'migrate.ps1')
        # Ollama's process can be running before its HTTP server is listening.
        $ready = $false
        for ($attempt = 0; $attempt -lt 30; $attempt++) {
            $saved = $ErrorActionPreference
            try {
                $ErrorActionPreference = 'Continue'
                & docker @script:ComposeArgs exec -T ollama ollama list 2>$null | Out-Null
                $ready = $LASTEXITCODE -eq 0
            } finally { $ErrorActionPreference = $saved }
            if ($ready) { break }
            Start-Sleep -Seconds 2
        }
        if (-not $ready) { throw 'Ollama did not start. Inspect its Docker logs.' }
        Write-Host "Preparing local model $Model (download on first launch)."
        Invoke-Compose @('exec', '-T', 'ollama', 'ollama', 'pull', $Model)
        Invoke-Compose @('up', '-d', '--build', '--no-deps', 'chat-api')
        Wait-ChatApi
        Write-Host 'API ready at http://127.0.0.1:5187. PostgreSQL, files and models use persistent volumes.'
    }
    if ($Action -in @('All', 'Client')) {
        Wait-ChatApi
        Invoke-Checked dotnet @('run', '--project', 'src/Alycia.Client.Avalonia')
    }
} finally { Pop-Location }
