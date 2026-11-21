#requires -Version 5.1
[CmdletBinding()]
param()
. (Join-Path $PSScriptRoot 'windows-common.ps1')
Push-Location $script:RepoRoot
try {
    $container = Invoke-Compose @('ps', '--status', 'running', '-q', 'postgres') -Capture
    if (-not $container) { throw 'PostgreSQL is not running. Start it with demo.ps1 -Action Api.' }
    $config = (Invoke-Compose @('config', '--format', 'json') -Capture) -join "`n" | ConvertFrom-Json
    $pg = $config.services.postgres.environment
    Invoke-Checked dotnet @('tool', 'restore')
    foreach ($module in @('Workspaces', 'Chat')) {
        $project = "src/Alycia.Modules.$module.Infrastructure"
        $context = "${module}DbContext"
        $sql = Join-Path ([IO.Path]::GetTempPath()) ("alycia-" + [Guid]::NewGuid().ToString('N') + '.sql')
        $remote = '/tmp/' + [IO.Path]::GetFileName($sql)
        try {
            Invoke-Checked dotnet @('restore', $project)
            Invoke-Checked dotnet @('tool', 'run', 'dotnet-ef', 'migrations', 'script', '--idempotent',
                '--project', $project, '--startup-project', $project, '--context', $context, '--output', $sql)
            if (-not (Test-Path $sql) -or (Get-Item $sql).Length -eq 0) { throw "Empty SQL for $module." }
            # Copy bytes instead of piping through PowerShell 5.1's default encoding.
            Invoke-Compose @('cp', $sql, "postgres:$remote")
            Invoke-Compose @('exec', '-T', 'postgres', 'psql', '-X', '-v', 'ON_ERROR_STOP=1',
                '-U', $pg.POSTGRES_USER, '-d', $pg.POSTGRES_DB, '-f', $remote)
        } finally {
            Remove-Item -LiteralPath $sql -Force -ErrorAction SilentlyContinue
            # Only remove the uniquely named temporary SQL created by this invocation.
            & docker @script:ComposeArgs exec -T postgres rm -f $remote | Out-Null
        }
    }
} finally { Pop-Location }
