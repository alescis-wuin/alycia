#requires -Version 5.1
[CmdletBinding()]
param([switch]$Integration)
. (Join-Path $PSScriptRoot 'windows-common.ps1')
if ($Integration -and [string]::IsNullOrWhiteSpace($env:ALYCIA_CHAT_TEST_CONNECTION)) {
    throw 'ALYCIA_CHAT_TEST_CONNECTION is required (test administrator with CREATEDB).'
}
Push-Location $script:RepoRoot
try {
    Invoke-Checked dotnet @('restore', 'Alycia.slnx', '--disable-parallel', '-m:1')
    Invoke-Checked dotnet @('tool', 'restore')
    Invoke-Checked dotnet @('build', 'Alycia.slnx', '--no-restore', '-m:1', '-p:UseSharedCompilation=false')
    & (Join-Path $PSScriptRoot 'quality.ps1') -Mode lint
    Invoke-Checked dotnet @('tool', 'run', 'dotnet-ef', 'migrations', 'has-pending-model-changes',
        '--project', 'src/Alycia.Modules.Chat.Infrastructure', '--context', 'ChatDbContext', '--no-build')
    Invoke-Checked dotnet @('test', 'Alycia.slnx', '--no-build', '--no-restore', '-m:1',
        '--logger', 'trx', '--results-directory', 'artifacts/tests')
    if (-not $env:ALYCIA_CHAT_TEST_CONNECTION) { Write-Host 'PostgreSQL integration tests skipped.' }
} finally { Pop-Location }
