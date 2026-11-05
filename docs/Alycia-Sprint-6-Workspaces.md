# Sprint 6 — Local Workspaces development and migrations

This guide documents the Sprint 6 create/read vertical slice. The approved
specification is [Alycia-Sprint-6-SMART.md](Alycia-Sprint-6-SMART.md).
This slice does **not** complete an MVP capability or implement authentication.

## Architecture and behavior

`Workspace` belongs only to the Workspaces module:

- **Domain:** `Workspace` owns its `Guid` identifier, trimmed nonempty `Name`
  (maximum 100 characters), and server-generated UTC `CreatedAtUtc`.
- **Application:** `CreateWorkspaceService` and `GetWorkspaceService` use the
  dedicated `IWorkspaceRepository`; no EF Core context is used by Application.
- **Infrastructure:** `EfWorkspaceRepository` and `WorkspacesDbContext` map to
  PostgreSQL table `workspaces.workspaces`. EF Core handles each ordinary
  `SaveChanges` operation. A unique index on the `citext` name column rejects
  case-insensitive duplicates, including concurrent inserts.
- **API:** `POST /api/workspaces` and `GET /api/workspaces/{id}` return the
  standard JSON envelope with `success`, `status`, `code`, `message`, `data`,
  `errors`, `traceId`, and `timestamp`. Relevant results include 201, 200, 400,
  404, 409, and 503. A malformed content type may return 415; nonlocal access
  returns 403 when the routes are enabled.
- **Avalonia:** local Workspace create/retrieve controls handle loading,
  success, validation, name conflicts, missing IDs, disabled/denied routes,
  and API/storage unavailability. Buttons are disabled while a request runs.

`GET /healthz` stays a technical plain-text endpoint independent of PostgreSQL;
it does not use the application JSON envelope.

## Security boundary — temporary Sprint 6 exception

Anonymous Workspace routes are **absent by default**. They are mapped only when
`ASPNETCORE_ENVIRONMENT=Development` **and** the independent setting
`ALYCIA_ENABLE_LOCAL_WORKSPACES=true` are both present. Production never maps
them, including when that setting is true.

When enabled, the API checks the actual TCP peer address and denies non-loopback
peers; it does **not** trust the `Host` or `X-Forwarded-For` headers. Run this
development API only on a host-bound loopback listener
(`http://127.0.0.1:5187`). This also prevents Docker-network clients from
reaching the listener. A Compose host-port mapping to `127.0.0.1` alone does
not prove internal-container access is restricted; do not enable anonymous
Workspace routes inside the existing network-reachable API container.

Use only nonsensitive development records. This exception is authorized for
Sprint 6 only; it is **not authentication, authorization, or a production
security design**, and must not automatically continue into later sprints.

## PostgreSQL and explicit migrations

Prerequisites: .NET 10 SDK, Docker Compose, PostgreSQL service from
`compose.yaml`, GNU Make, and the repository's local `dotnet-ef` manifest.
The PostgreSQL port remains unpublished on the host.

```bash
# Run on the developer machine, from the repository root.
docker compose up -d postgres
dotnet tool restore
make migrate
```

`make migrate` is the **only supported entry point** for applying module
migrations. It runs `scripts/migrate.sh`, generates idempotent EF Core migration
SQL, then executes it using `psql` in the running PostgreSQL container with
`ON_ERROR_STOP=1`. It does **not** migrate automatically on API startup.
The Workspaces history is stored in `workspaces."__EFMigrationsHistory"`.
The initial schema installs the PostgreSQL `citext` extension at database scope.

S6 registers only `workspaces` in the migration script. New module contexts
must be added deliberately in dependency order; ordering does **not** provide
one atomic transaction across modules. If a future module migration fails,
the command stops, but prior successful module migrations may remain applied.
Inspect migration history and repair the affected module before retrying;
never delete named volumes as a migration workaround. `make migrate` can be
repeated: already-recorded migrations are not reapplied. It returns a nonzero
status on generation/SQL execution failures.

## Local host API and Avalonia

Keep the Compose `postgres` service running. The following example derives
an Npgsql connection string from the **local** Compose configuration and the
container bridge IP, without echoing the password to the terminal. It requires
Python 3 and a reachable Docker bridge. The password is held in a process
environment variable: do not print it, capture it in logs, or paste it in
issues. This is a local development workflow, not a production secret design.

```bash
connection_string="$(python3 - <<'PY'
import json
import subprocess

config = json.loads(subprocess.check_output(
    ['docker', 'compose', 'config', '--format', 'json'], text=True))
env = config['services']['postgres']['environment']
if isinstance(env, list):
    env = dict(item.split('=', 1) for item in env)
password = env['POSTGRES_PASSWORD'].replace('"', '""')
container = subprocess.check_output(
    ['docker', 'compose', 'ps', '-q', 'postgres'], text=True).strip()
if not container:
    raise SystemExit('PostgreSQL container is not running')
address = subprocess.check_output([
    'docker', 'inspect', '--format',
    '{{range .NetworkSettings.Networks}}{{println .IPAddress}}{{end}}',
    container], text=True).splitlines()[0].strip()
print(f'Host={address};Port=5432;Database=alycia;Username=alycia;'
      f'Password="{password}";Timeout=15;Command Timeout=15')
PY
)" || { echo 'Failed to construct the local connection string' >&2; exit 1; }
export ALYCIA_WORKSPACES_CONNECTION="$connection_string"
unset connection_string

# Terminal 1: run the API on the host, not inside Docker.
ASPNETCORE_ENVIRONMENT=Development \
ALYCIA_ENABLE_LOCAL_WORKSPACES=true \
  dotnet run --no-launch-profile \
  --project src/Alycia.Api/Alycia.Api.csproj -- \
  --urls http://127.0.0.1:5187
```

In a separate terminal, start Avalonia against the same loopback address:

```bash
ALYCIA_API_BASE_URL=http://127.0.0.1:5187 \
  dotnet run --project src/Alycia.Client.Avalonia/Alycia.Client.Avalonia.csproj
```

Create a nonsensitive Workspace, record its UUID, retrieve it, and verify
name-conflict/invalid-input feedback. Restart the API with the same opt-in
configuration; the UUID remains retrievable from PostgreSQL. Remove the opt-in
or run in Production to observe absent routes while `/healthz` continues to
work. Stop the host API to observe an unavailable state in the client.

For repeatable negative controls, separately verify that the Development API
without the opt-in and the Production API with the opt-in both return 404 for
Workspace routes. When enabled locally, Docker clients must not reach the host
loopback listener. Do not test external accessibility by exposing anonymous
business routes to a LAN, VPN, or public interface.

## Tests, diagnostics, and boundaries

Run `make check` for formatting and the solution's xUnit suites. These tests
do not replace the real PostgreSQL and graphical integration checks.
Use `make migrate` only when an actual schema change must be applied.

In Sprint 6 local checks, the owner observed an idempotent initial migration,
case-insensitive database uniqueness, two concurrent requests yielding 201/409
with exactly one row, 503 on simulated PostgreSQL unavailability, and response
trace IDs matching Serilog JSON Lines request events (including durations).
The owner also confirmed the actual Avalonia create/read/error/restart flow.
These results are evidence of the tested configurations, not a general claim
of production security or performance.

One host-side connection diagnostic took approximately 7.8 seconds to complete
Npgsql authentication plus `SELECT 1` using the Docker bridge address. The
cause of this latency was **not established**; local verification used a
15-second connection timeout. Treat performance and connectivity across
other host/Docker configurations as unverified.

Never log raw connection strings, passwords, request bodies, or sensitive
Workspace names. S6 has no authentication, MAUI parity, multiuser support,
other module persistence, CI/CD, or production business routes. The S6
exception must be revisited before subsequent Identity-protected API work.
