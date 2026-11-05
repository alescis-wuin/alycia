# Alycia — Sprint 1: Approved SMART Specification

**Status:** APPROVED — `C10.P05`
**Approval date:** November 5, 2026
**Reference source:** `Alycia-Sprint-0-Cadrage-consolide-R5.md`, § 13.1.
**Nature:** Standalone, faithful extract of the approved specification; no new technical decisions.
**Execution:** Not started at the time this document was written.

---

## Sprint 1 — Solution and Infrastructure Foundation

**Reference:** `C10.P05` — **APPROVED** on November 5, 2026.
**Planned branch:** `setup/solution-foundation`.
**Initial environment:** CachyOS / Linux.
**Reference duration:** 30 to 90 minutes per work session; continue beyond a session rather than declaring premature completion.

### SMART Objective

Create Alycia's new, independent technical foundation, comprising a .NET 10 solution, an ASP.NET Core API, PostgreSQL, Docker Compose, structured logging, a Makefile, and initial documentation.

**Measurable outcome:** From an environment meeting the prerequisites, the user can build the solution, start the API and PostgreSQL, verify that they work, inspect logs, and stop the services without destroying persistent volumes.

This sprint does not complete any of the MVP's eight business capabilities.

### Prerequisites

- A new Alycia repository, separate from KnowledgeHub, created by the user.
- .NET 10 SDK.
- Docker with Docker Compose.
- GNU Make and Bash.
- Git.
- Access to the required dependencies and container images for initial installation.
- **Serilog and the technical `/healthz` contract already approved.**

### Deliverables

- `Alycia.slnx`.
- `src/Alycia.Api/`: **the only initial .NET project**.
- `Dockerfile`.
- `compose.yaml`.
- `Makefile`.
- `.env.example`.
- `.gitignore`.
- `README.md`, in English.
- Serilog configuration for console and JSON Lines file logging.
- Technical endpoint `GET /healthz`.
- Persistent PostgreSQL volume.
- Persistent log storage.

### Included Scope

**API**

- Minimal, buildable, and runnable ASP.NET Core 10 application.
- Unauthenticated `GET /healthz`.
- Response `200 OK` with the plain-text body `Healthy` when the application responds and its registered health checks succeed; response `503` if a health check fails.
- Technical contract separate from the application JSON response envelope.
- No access to business data.
- In Sprint 1, `/healthz` checks only API health, **not PostgreSQL availability**.

**Infrastructure**

- PostgreSQL deployed through Docker Compose.
- Persistent named volume for the database.
- No PostgreSQL port exposed publicly.
- API host port published only on `127.0.0.1`.
- Local configuration for secrets and sensitive settings kept outside Git.
- Backend runnable directly with .NET during development.
- Within Docker, the API may listen on an internal container interface: the network restriction applies to the host-side port publication.

**Logging**

- Serilog integrated with `ILogger`.
- Timestamped, colorized console output when supported by the terminal.
- Persistent JSON Lines logs.
- Time-based rotation and file-size limit.
- Limited, configurable retention.
- HTTP events including trace identifier, method, path, status, and elapsed time.
- No secrets, tokens, HTTP bodies, or sensitive headers in logs.

**Makefile**

- `make build`: build the solution.
- `make up`: build and start Docker services.
- `make status`: display service status.
- `make down`: stop services without deleting volumes.

**Documentation**

- Prepare a README covering prerequisites, local configuration, startup, Makefile commands, API and PostgreSQL checks, logs, shutdown, and known limitations.

### Out of Scope

- Business entities and rules.
- EF Core `DbContext`, repositories, and migrations.
- Business controllers and application DTOs.
- Authentication and sessions; secrets vault.
- Avalonia and MAUI clients.
- AI providers, documents, and RAG.
- Full backups, centralized monitoring, and CI/CD.
- No additional .NET project created merely to prepare for future work.

### Ten Acceptance Criteria

1. `Alycia.slnx` contains `Alycia.Api` and builds with .NET 10.
2. `docker compose config --quiet` succeeds.
3. The API and PostgreSQL start without blocking errors.
4. `GET /healthz` returns HTTP `200` and `Healthy` over the loopback interface.
5. `pg_isready`, executed inside the PostgreSQL container, confirms that the server accepts connections.
6. HTTP events appear in a valid JSON Lines file with trace identifier, status, and elapsed time.
7. Logs persist, and their configuration includes rotation and limited retention.
8. The API is not published on external interfaces; PostgreSQL is not publicly exposed.
9. `make build`, `make up`, `make status`, and `make down` work; `make down` preserves volumes.
10. The README enables reproduction of the required operations, without real secrets in Git-tracked files.

### Targeted Verification

**Build and configuration:** `dotnet build`, `docker compose config --quiet`, and confirmation that only the intended .NET project exists.

**Integration and execution:** Actual startup of both services; local HTTP request to `/healthz`; `pg_isready` inside PostgreSQL; inspection of valid JSON Lines and required fields; verification of published network ports; volumes preserved after `make down`; Makefile commands.

**Unit tests:** **None required**, because no business rules are introduced.

**Automated integration tests:** No additional automated test project in Sprint 1. Integration checks run directly against the started services.

**Manual checks:** Log readability, README reproducibility, useful failure messages, absence of secrets in output, and non-destructive shutdown.

**Excluded from testing:** Persistence of a business entity after restart, EF Core/migrations, authentication/authorization, graphical testing, AI providers, backup/restore, load, and performance.

### Implementation Steps

1. The user creates the repository, solution, and `Alycia.Api`.
2. Configure Serilog and `/healthz`.
3. Add Dockerfile, Compose, PostgreSQL, and volumes.
4. Add Makefile, local configuration, and documentation.
5. Run targeted checks, diagnose any failures, and correct them.
6. Produce the completion report with evidence of operation and remaining limitations.

### Completion Condition

**All ten acceptance criteria must be verified in the actual environment.** A check that has not been performed is not considered successful; no blocking failure may be ignored. If necessary, continue Sprint 1 without starting another workstream.

**MVP progress after this sprint, if validated:** still `0/8` completed business capabilities.
