#requires -Version 5.1
[CmdletBinding()]
param([ValidateSet('format', 'lint', 'install-hooks')][string]$Mode)
. (Join-Path $PSScriptRoot 'windows-common.ps1')
Push-Location $script:RepoRoot
try {
    if ($Mode -eq 'install-hooks') {
        $existing = & git config --get core.hooksPath
        if ($existing -eq '.githooks') { Write-Host 'Git hooks already installed.'; return }
        if ($existing) { throw 'An existing core.hooksPath is configured; it was preserved.' }
        $hooks = Invoke-Checked git @('rev-parse', '--git-path', 'hooks') -Capture
        if (@(Get-ChildItem $hooks -ErrorAction SilentlyContinue | Where-Object { $_.Name -notlike '*.sample' }).Count -gt 0) {
            throw 'Existing Git hooks were preserved.'
        }
        Invoke-Checked git @('config', '--local', 'core.hooksPath', '.githooks')
        return
    }
    $files = @((Invoke-Checked git @('-c', 'core.quotepath=false', 'ls-files', '--modified', '--others', '--exclude-standard') -Capture))
    if ($Mode -eq 'lint') {
        $files += @(Invoke-Checked git @('-c', 'core.quotepath=false', 'diff', '--cached', '--name-only', '--diff-filter=ACM') -Capture)
    }
    $files = @($files | Sort-Object -Unique | Where-Object {
        (Test-Path -LiteralPath $_ -PathType Leaf) -and
        ($_ -match '\.(cs|csproj|slnx|json|md|ya?ml|axaml|xml|sh|ps1)$' -or $_ -in @('Makefile', '.editorconfig', '.gitignore', '.githooks/pre-commit'))
    })
    $cs = @($files | Where-Object { $_.EndsWith('.cs') })
    if ($cs.Count -gt 0) {
        $arguments = @('format', 'whitespace', 'Alycia.slnx', '--no-restore')
        if ($Mode -eq 'lint') { $arguments += '--verify-no-changes' }
        Invoke-Checked dotnet ($arguments + @('--include') + $cs)
    }
    foreach ($file in $files) {
        $text = [IO.File]::ReadAllText((Join-Path $script:RepoRoot $file))
        $clean = [regex]::Replace($text, '(?m)[\t ]+(?=\r?$)', '')
        if ($clean.Length -gt 0 -and -not $clean.EndsWith("`n")) { $clean += "`n" }
        if ($text -ne $clean) {
            if ($Mode -eq 'lint') { throw "Whitespace error: $file" }
            $bytes = [IO.File]::ReadAllBytes((Join-Path $script:RepoRoot $file))
            $bom = $bytes.Length -ge 3 -and $bytes[0] -eq 239 -and $bytes[1] -eq 187 -and $bytes[2] -eq 191
            [IO.File]::WriteAllText((Join-Path $script:RepoRoot $file), $clean, (New-Object Text.UTF8Encoding($bom)))
        }
    }
    if ($Mode -eq 'lint') {
        Invoke-Checked git @('diff', '--check')
        Invoke-Checked git @('diff', '--cached', '--check')
    }
    Write-Host "Quality $Mode passed."
} finally { Pop-Location }
