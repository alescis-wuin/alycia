# Windows launcher

Branch: `prototype/conversation-files`. Target: Windows 11 x64, Windows PowerShell
5.1 or PowerShell 7, Docker Desktop with Linux containers. GNU Make, Bash, Python
and a native Ollama installation are not required for this workflow.

## One-command setup and launch

Paste this **single command into PowerShell** under your usual Windows account:

```powershell
& { $ErrorActionPreference = 'Stop'; $bootstrap = Join-Path $env:TEMP ('alycia-' + [guid]::NewGuid() + '.ps1'); Invoke-WebRequest -UseBasicParsing 'https://raw.githubusercontent.com/alescis-wuin/alycia/prototype/conversation-files/scripts/bootstrap-windows.ps1' -OutFile $bootstrap; try { powershell.exe -NoProfile -ExecutionPolicy Bypass -File $bootstrap; if ($LASTEXITCODE -ne 0) { throw 'Setup stopped. Read the message above, then rerun this command.' } } finally { Remove-Item -LiteralPath $bootstrap -Force } }
```

This downloads the repository's bootstrap into a temporary file and runs it in a
separate PowerShell process. Execution policy is changed only for that process.
The bootstrap:

1. Checks Git and installs it with WinGet if missing.
2. Clones this branch into `$HOME\alycia`, or updates that checkout by fast-forward.
   An unrelated repository, dirty checkout or divergent history is never overwritten.
3. Checks/installs .NET SDK 10 and Docker Desktop through WinGet, refreshes PATH,
   and checks Docker Compose.
4. Checks WSL when Docker is unavailable; if WSL is missing/too old for `wsl
   --version`, requests elevation to run `wsl --install --no-distribution`, then
   stops with instructions to restart Windows and run the same command again.
5. Starts Docker Desktop, waits for its Linux engine, creates `.env` secrets only
   where absent and preserves the existing PostgreSQL password.
6. Starts PostgreSQL and Ollama, verifies database credentials and applies both
   modules' idempotent EF migrations.
7. Downloads `qwen2.5:1.5b` into Ollama's persistent Docker volume, builds/starts
   the API, checks authenticated database/model endpoints and opens Avalonia.

Complete any Windows elevation, Docker Desktop first-run or restart prompts.
There is no automatic reboot. If WinGet is absent, install/update Microsoft's
**App Installer** first. Virtualization must be available and enabled; corporate
device policies can require an administrator. Package, image and model downloads
need internet access and can take several minutes. After setup, inference is local.

The baseline uses CPU inference so it works without GPU setup. Existing host-side
Ollama models are not copied into the Docker volume. GPU acceleration and Windows
ARM64 have not been validated in this prototype.

For a different checkout path/model, download the bootstrap and pass its optional
`-Destination 'C:\Projects\Alycia' -Model 'your-local-model:tag'` parameters.

## Relaunch and maintenance

From the cloned repository:

```powershell
# Start/reuse all services and open the client.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/demo.ps1

# Separate API startup and client windows.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/demo.ps1 -Action Api
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/demo.ps1 -Action Client

# Inspect or stop this demo; persistent volumes are retained.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/demo.ps1 -Action Status
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/demo.ps1 -Action Stop

# Explicit migrations, after PostgreSQL is running.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/migrate.ps1
```

Closing Avalonia does not stop Docker services. Stop the previous API if it already
occupies port 5187. If a previous Linux/host-network `chat-api` container exists in
the same Compose project, stop it before switching launchers.

If GNU Make is installed, `make demo-windows` checks/installs dependencies and
launches everything. `make demo-api`, `make demo-client`, `make migrate`,
`make format`, `make lint`, and `make install-hooks` select PowerShell on Windows.
`make demo-windows-stop` and `make demo-windows-status` control this demo.
The existing `make up/down/status` targets still control the foundational
`compose.yaml` services, not the complete Windows demo.

## Network and persistence

Windows uses `compose.yaml` + `compose.windows.yaml`. Do not combine the Windows
file with the Linux `compose.demo.yaml` host-network override.

- API: `127.0.0.1:5187` on Windows, forwarded to the API container's bridge port.
- PostgreSQL and Ollama: Compose network only, with no Windows host port exposed.
- Ollama URL inside the API: `http://ollama:11434/`.
- Persistent volumes: `postgres_data`, `chat_files`, `ollama_data`, `api_logs`, each
  with Compose's project prefix. No launcher deletes them.
- Chat/file endpoints still require Development and explicit opt-in. In Windows
  container mode, they additionally require the random bearer token generated in
  `.env` (`ALYCIA_CHAT_ACCESS_TOKEN`). Browser Origin headers are still rejected.
- The native client receives that token and the loopback API URL only in its
  process environment. Use `demo.ps1 -Action Client` rather than a bare `dotnet run`
  so the token is loaded. Do not print/share `.env` or full Compose configuration.
- Linux's existing launcher retains its actual-loopback-peer check and host Ollama.

The token is a local launcher credential, not multi-user authentication. Anyone
with access to the local `.env` or Docker daemon can access this prototype's data.
An absent/invalid token prevents container-mode API startup or denies requests;
the API does not silently fall back to trusting a Docker gateway address.

Migration SQL is copied byte-for-byte into a uniquely named temporary container
file and applied with `psql -v ON_ERROR_STOP=1`. This avoids Windows PowerShell 5.1
pipeline encoding issues. Temporary SQL is then removed. Migrations preserve the
existing conversation/file data; there is no database reset step.

If PostgreSQL authentication fails, use the existing volume's password in `.env`.
Changing the environment file does not change an initialized database password.
If Docker is still unavailable after installation, finish its first-run setup,
update WSL with `wsl --update` if requested, restart Windows when needed, and rerun.

## Verification and remaining checks

- Solution build: no warnings/errors; 69 xUnit tests passing with PostgreSQL 17
  integration enabled, including token/Origin/peer/provider restrictions.
- Windows x64 framework-dependent client publish completed and produced a PE
  executable. This is cross-compilation, not an actual Windows UI launch.
- PowerShell syntax parsed and helper tests executed with PowerShell 7 under Linux:
  secret generation/reuse, preservation and escaping of existing passwords,
  native exit-code handling, paths with spaces and Compose network exposure.
- Real HTTP API + PostgreSQL + simulated Ollama validated authenticated chat/file
  flows, rejection of missing/wrong tokens and browser Origin, and persistence.
- Both Compose variants validated with `docker compose config`; Make recipes
  checked for both OS selections.

Windows PowerShell 5.1, WinGet/UAC/WSL installation, Docker Desktop runtime and the
native Windows pickers/drag-and-drop cannot be executed in the Linux verification
environment. Confirm them on the target machine; no claim of a completed Windows
end-to-end run is made.

Run the script helper regression checks without starting a Docker daemon:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/tests/windows-scripts.Tests.ps1
```

Upstream references: [Docker Desktop Windows prerequisites](https://docs.docker.com/desktop/setup/install/windows-install/),
[.NET SDK installation](https://learn.microsoft.com/dotnet/core/install/windows),
[WSL commands](https://learn.microsoft.com/windows/wsl/basic-commands), and
[official Ollama Docker image](https://hub.docker.com/r/ollama/ollama/).
