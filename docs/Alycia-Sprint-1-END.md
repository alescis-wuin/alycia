# Alycia — Sprint 1 End Report

**Sprint:** Sprint 1 — Solution and Infrastructure Foundation
**Approved specification:** `C10.P05` (`docs/Alycia-Sprint-1-SMART.md`)
**Report date:** November 5, 2026
**Status:** Completed — all ten acceptance criteria supported by recorded local evidence
**Repository:** `/developpement/seynax/Alycia`
**Branch:** `setup/solution-foundation`
**Published software version:** None
**MVP progress:** 0/8 business capabilities completed

## 1. Delivered Scope

The independent Alycia foundation was created and validated on CachyOS. It consists of:

- `Alycia.slnx`, containing a single .NET 10 project: `src/Alycia.Api/Alycia.Api.csproj`.
- A minimal ASP.NET Core API with the unauthenticated, technical `GET /healthz` endpoint; its health check does not depend on PostgreSQL.
- Serilog integrated with `ILogger`, timestamped console events, structured JSON Lines files, request tracing, HTTP status codes, and elapsed durations.
- A multi-stage .NET 10 `Dockerfile`, `compose.yaml`, PostgreSQL 17, and named volumes for the database and API logs.
- Local-only API port publication and no PostgreSQL host port publication.
- `Makefile` targets for `build`, `up`, `status`, and `down`.
- `.env.example`, `.gitignore`, `.dockerignore`, and an English `README.md`.
- The approved Sprint 1 SMART specification, recorded in English as `docs/Alycia-Sprint-1-SMART.md`.

No business entity, EF Core `DbContext`, migration, authentication, graphical client, or CI/CD pipeline was introduced.

## 2. Environment and Git Evidence

The initial prerequisite check reported:

| Tool | Observed result |
| --- | --- |
| Operating system | CachyOS |
| .NET SDK | `10.0.403` |
| Docker CLI / Engine | `29.9.2` / `29.9.2` |
| Docker Compose | `5.7.0` |
| GNU Make | `4.4.1` |
| Git, Bash, curl | Available |

Recorded local commits:

```text
945a482 (HEAD -> setup/solution-foundation) docs(solution-foundation): add approved Sprint 1 SMART specification
0035ec2 setup(solution-foundation): establish .NET and Docker infrastructure
```

The first commit recorded 13 foundation files (347 insertions). The second added the 144-line English SMART document. No Git remote was configured in the consolidated audit. The final Git inspection showed a clean worktree:

```text
## setup/solution-foundation
```

## 3. Acceptance Criteria — AC01 to AC10

| Criterion | Result | Recorded verification |
| --- | --- | --- |
| **AC01** — .NET 10 solution and API build | **PASS** | `dotnet sln Alycia.slnx list` showed only `src/Alycia.Api/Alycia.Api.csproj`; `make build` completed with 0 warnings and 0 errors. |
| **AC02** — Valid Compose configuration | **PASS** | `docker compose config --quiet` succeeded. |
| **AC03** — API and PostgreSQL startup | **PASS** | `make up` built the API image; `docker compose ps` showed the API running and PostgreSQL `healthy`. |
| **AC04** — Local `/healthz` | **PASS** | `curl` returned HTTP `200 OK` and `Healthy` from `127.0.0.1:8080`. |
| **AC05** — PostgreSQL readiness | **PASS** | `docker compose exec -T postgres pg_isready -U alycia -d alycia` reported `accepting connections` and exit code 0. |
| **AC06** — Structured HTTP logs | **PASS** | JSON Lines parsing succeeded; the consolidated audit found 11 valid events and 2 `/healthz` requests with `TraceId`, `StatusCode`, and numeric `Elapsed`. |
| **AC07** — Persistent logs and configured rotation/retention | **PASS** | Both named volumes existed; the previous log event was still present after `make down` and `make up`. Daily and size-based rolling and limited retention were verified in configuration. |
| **AC08** — Restricted network exposure | **PASS** | A focused Docker container inspection verified that API port 8080 was bound exclusively to `127.0.0.1:8080` and PostgreSQL port 5432 had no host binding. |
| **AC09** — Makefile and preserved volumes | **PASS** | `make build`, `make up`, `make status`, and `make down` succeeded; both named volumes remained after `make down`. |
| **AC10** — README and secret handling | **PASS** | README operational instructions were inspected; `.env` was ignored and not tracked. The consolidated audit found the known local PostgreSQL password absent from all 13 files tracked at that time. |

### Selected terminal evidence

**Build**

```text
Alycia.Api -> /developpement/seynax/Alycia/src/Alycia.Api/bin/Debug/net10.0/Alycia.Api.dll
La génération a réussi.
    0 Avertissement(s)
    0 Erreur(s)
```

**Service status**

```text
alycia-api-1       Up                 127.0.0.1:8080->8080/tcp
alycia-postgres-1  Up (healthy)       5432/tcp
```

**HTTP and database readiness**

```text
HTTP/1.1 200 OK
Healthy
/var/run/postgresql:5432 - accepting connections
```

**Structured logging**

```text
Valid JSON events: 11
Validated HTTP requests: 2
TraceId, StatusCode, Elapsed: OK
```

Log settings observed inside the API container:

```text
ALYCIA_LOG_FILE_SIZE_BYTES = 10485760
ALYCIA_LOG_RETAINED_FILES = 14
```

**Persistence**

```text
alycia_postgres_data (driver: local)
alycia_api_logs (driver: local)
PASS: Previous log event preserved after restart
```

**Actual network bindings (focused recheck)**

```text
API bindings: {'8080/tcp': [{'HostIp': '127.0.0.1', 'HostPort': '8080'}]}
PostgreSQL bindings: {'5432/tcp': None}
AC08: PASS
```

**API outside Docker**

```text
PASS: Direct .NET API responds Healthy
```

This last check ran the built API DLL on the temporary local port `18080`, then stopped the temporary process. It demonstrated that the API can run outside Docker without checking the exact `dotnet run` command documented in the README.

## 4. Consolidated Audit and Follow-up

The consolidated evidence file was generated at **2026-11-05 02:10:43 UTC**:

`Alycia-Sprint-1-Evidence-20261105T021043Z.txt`

Its original summary was:

```text
Checks passed: 11
Checks failed: 2
FINAL RESULT: FAIL - review failed checks
```

The two reported failures were investigated separately instead of rerunning successful checks:

1. **Git working tree:** The audit showed an untracked `docs/` directory. The approved English SMART document was subsequently committed as `945a482`, and the final `git status --short --branch` output showed a clean worktree.
2. **AC08 network test:** The audit script interpreted `docker compose port postgres 5432` returning `:0` as a published port. This was not evidence of an actual host binding. A targeted `docker inspect` of `.NetworkSettings.Ports` showed `{'5432/tcp': None}`, while the API binding was exactly `127.0.0.1:8080`. The corrected assessment was **AC08: PASS**.

The initial audit's failure summary is retained as historical evidence; the focused follow-up resolves both findings.

## 5. Tests Executed and Limits

Executed on the owner's CachyOS environment, as evidenced by supplied terminal output:

- .NET solution build and Docker image build.
- Compose configuration and actual API/PostgreSQL startup.
- Local HTTP health response and `pg_isready` inside PostgreSQL.
- JSON Lines parsing and required HTTP log fields.
- Inspection of logging settings, Docker volumes, actual port bindings, and Compose service state.
- `make down` / `make up` with volume preservation and recovery of a previous log event.
- Direct execution of the compiled API outside Docker.
- Makefile operations, Git status and history, README coverage, and known local PostgreSQL-secret exclusion.

Not executed or not demonstrated:

- A timed or size-triggered log rollover or retention purge; their configuration was inspected, not their eventual activation.
- A fresh-machine reproduction solely by following README instructions.
- A general secrets audit; the scan checked the known local PostgreSQL password in the files tracked at audit time.
- Unit-test projects or automated integration-test suites, neither of which was required for this technical foundation sprint.
- EF Core persistence, business functionality, authentication, graphical client tests, backup/restore, performance tests, or Windows/MAUI verification, all outside Sprint 1 scope.

The last observed Docker state was running, with PostgreSQL healthy. No remote push or software release was recorded.

## 6. Closure

**Sprint 1 — Solution and Infrastructure Foundation: completed.** All ten criteria defined in the approved `C10.P05` specification have supporting local evidence, including the corrected AC08 assessment. The work was committed locally on `setup/solution-foundation`; no additional business capability was completed.

**MVP progress remains 0/8.** The next planned sprint is **Sprint 2 — Avalonia Backend Status**, which was not started as part of this work.
