#requires -Version 5.1
[CmdletBinding()]
param([switch]$Integration)
. (Join-Path $PSScriptRoot 'windows-common.ps1')
if ($Integration -and [string]::IsNullOrWhiteSpace($env:ALYCIA_CHAT_TEST_CONNECTION)) {
    throw 'ALYCIA_CHAT_TEST_CONNECTION is required (test administrator with CREATEDB).'
}
Push-Location $script:RepoRoot
try {
    $runDir = Join-Path ([IO.Path]::GetTempPath()) ('alycia-check-' + [Guid]::NewGuid())
    $capture = Join-Path $runDir 'architecture'
    $results = Join-Path $runDir 'tests'
    $python = Get-Command python -ErrorAction SilentlyContinue
    $isolation = @('scripts/build-isolated-modules.py','--require-complete')
    if (-not $Integration -and (-not $env:ALYCIA_CHAT_TEST_CONNECTION -or -not $env:ALYCIA_RERANK_DIRECTORY)) {
        $isolation += '--build-only'
    }
    $guard = @('scripts/check-modules.py','--all-projects','--capture',$capture)
    if ($python) {
        Invoke-Checked $python.Source $isolation
        Invoke-Checked $python.Source $guard
    } else {
        Invoke-Checked py (@('-3') + $isolation)
        Invoke-Checked py (@('-3') + $guard)
    }
    Invoke-Checked dotnet @('restore', 'Alycia.slnx', '--locked-mode', '--configfile', 'NuGet.Config', '--disable-parallel', '-m:1')
    Invoke-Checked dotnet @('tool', 'restore')
    Invoke-Checked dotnet @('build', 'Alycia.slnx', '--no-restore', '-m:1', '-p:UseSharedCompilation=false')
    & (Join-Path $PSScriptRoot 'quality.ps1') -Mode lint
    Invoke-Checked dotnet @('tool', 'run', 'dotnet-ef', 'migrations', 'has-pending-model-changes',
        '--project', 'src/Alycia.Persistence.Migrations', '--context', 'ChatDbContext', '--no-build')
    Invoke-Checked dotnet @('tool', 'run', 'dotnet-ef', 'migrations', 'has-pending-model-changes',
        '--project', 'modules/workspaces/src/Alycia.Modules.Workspaces.Infrastructure', '--context', 'WorkspacesDbContext', '--no-build')
    Invoke-Checked dotnet @('test', 'Alycia.slnx', '--no-build', '--no-restore', '-m:1',
        '--logger', 'trx', '--results-directory', $results)
    New-Item -ItemType Directory -Force 'artifacts/tests' | Out-Null
    Copy-Item (Join-Path $results '*.trx') 'artifacts/tests'
    if ($Integration) {
        $audit = @('scripts/architecture/finition-audit.py', $capture, $results, 'artifacts/architecture', '--require-complete')
        if ($python) { Invoke-Checked $python.Source $audit } else { Invoke-Checked py (@('-3') + $audit) }
    }
    if (-not $env:ALYCIA_CHAT_TEST_CONNECTION) { Write-Host 'PostgreSQL integration tests skipped.' }
} finally {
    if ($runDir -and (Test-Path -LiteralPath $runDir)) { Remove-Item -LiteralPath $runDir -Recurse -Force }
    Pop-Location
}
