# Alycia — Sprint 1: Approved SMART Sheet (Revision 5)

**Status:** APPROVED — `C10.P05`

**Approval date:** November 5, 2026

**Source references:** `Alycia-Sprint-0-Cadrage-consolide-R9.md`, § 13.1; Alycia Charter R1.6, § 7. **`C10.P05` remains the originally approved SMART contract.**

**Nature:** approved `C10.P05` SMART sheet; transitional amendment: the remote repository, protected branches and issues move to Sprint 2 without changing the ten technical acceptance criteria.

**Execution:** determine the current state from the local repository and evidence provided by the owner; this document alone does not establish that the sprint was completed.

---

## Sprint 1 — Solution and Infrastructure Foundation

**Reference:** `C10.P05` — **APPROVED** on November 5, 2026.

**Originally planned branch:** `setup/solution-foundation`; verify the branch and history actually used.

**PR:** target `develop` if this remains possible without rewriting history; otherwise document the transitional exception during Sprint 2.

**Issues:** create them **retrospectively during Sprint 2** if absent; do not invent their numbers or URLs.

**Initial environment:** CachyOS / Linux.

**Reference duration:** 30–90 minutes per work session; extend the sprint without declaring it artificially complete if necessary.

### SMART Objective

Create the new, independent Alycia technical foundation, consisting of a .NET 10 solution, an ASP.NET Core API, PostgreSQL, Docker Compose, structured logging, a Makefile and initial documentation.

**Measurable result:** from an environment meeting the prerequisites, the owner can build the solution, start the API and PostgreSQL, check that they work, inspect the logs and stop the services without destroying persistent volumes.

This sprint completes none of the eight MVP business capabilities.

### Prerequisites

- A **local** Alycia repository, created by the owner and separate from KnowledgeHub; the GitHub remote is handled during Sprint 2.
- .NET 10 SDK.
- Docker with Docker Compose.
- GNU Make and Bash.
- Git.
- Access to dependencies and images required for initial setup.
- **Serilog and the technical `/healthz` contract already approved.**
- **No retroactive GitHub prerequisite:** `origin`, three protected branches, issues and the first PR are regularized during Sprint 2. Previously performed Git checks remain valid if supported by evidence.

### Deliverables

- `Alycia.slnx`.
- `src/Alycia.Api/`: the **only initial .NET project**.
- `Dockerfile`.
- `compose.yaml`.
- `Makefile`.
- `.env.example`.
- `.gitignore`.
- `README.md`, in English.
- Serilog configuration for console and JSON Lines files.
- Technical `GET /healthz` endpoint.
- Persistent PostgreSQL volume.
- Persistent log storage.

### Included Scope

**API**

- Minimal ASP.NET Core 10 application that builds and runs.
- Unauthenticated `GET /healthz`.
- `200 OK` with the text body `Healthy` when the application responds and its registered health checks pass; `503` when a health check fails.
- A technical contract separate from the application JSON response envelope.
- No access to business data.
- During Sprint 1, `/healthz` checks the health of the API **only, not PostgreSQL availability**.

**Infrastructure**

- PostgreSQL deployed through Docker Compose.
- Persistent named volume for the database.
- No publicly exposed PostgreSQL port.
- Publish the API host port on `127.0.0.1` only.
- Keep local configuration of secrets and sensitive settings outside Git.
- Allow the backend to run directly with .NET during development.
- Within Docker, the API may listen on an internal container interface: the network restriction applies to the published host port.

**Logging**

- Serilog integrated with `ILogger`.
- Timestamped console logs, with color when supported by the terminal.
- Persistent JSON Lines logs.
- Time-based rotation and a file-size limit.
- Bounded, configurable retention.
- HTTP events including trace ID, method, path, status and duration.
- No secrets, tokens, HTTP bodies or sensitive headers in logs.

**Makefile**

- `make build`: build the solution.
- `make up`: build and start Docker services.
- `make status`: display service status.
- `make down`: stop services without removing volumes.

**Documentation**

- Prepare a README covering prerequisites, local configuration, startup, Makefile, API and PostgreSQL checks, logs, shutdown and known limitations.

### Excluded Scope

- Business entities and rules.
- EF Core `DbContext`, repositories and migrations.
- Business controllers and application DTOs.
- Authentication and sessions; secrets vault.
- Avalonia and MAUI clients.
- AI providers, documents and RAG.
- Full backups, centralized monitoring and CI/CD.
- No additional .NET project created merely to anticipate future work.

### Ten Acceptance Criteria

| ID | Required observable result |
|---|---|
| **AC01** | `Alycia.slnx` contains `Alycia.Api` and builds under .NET 10. |
| **AC02** | `docker compose config --quiet` succeeds. |
| **AC03** | The API and PostgreSQL start without blocking errors. |
| **AC04** | `GET /healthz` returns HTTP `200` and `Healthy` through the local loopback interface. |
| **AC05** | `pg_isready`, executed inside the PostgreSQL container, confirms the server accepts connections. |
| **AC06** | HTTP events are present in a valid JSON Lines file with trace, status and duration. |
| **AC07** | Logs persist and their configuration includes rotation and bounded retention. |
| **AC08** | The API is not published on external interfaces; PostgreSQL is not publicly exposed. |
| **AC09** | `make build`, `make up`, `make status` and `make down` work; `make down` preserves volumes. |
| **AC10** | The README allows the planned operations to be reproduced, with no real secrets in Git-tracked files. |

### Targeted Checks

**Build and configuration:** `dotnet build`, `docker compose config --quiet`, and confirmation that only the intended .NET project exists.

**Integration and execution:** actually start both services; check local HTTP `/healthz`; run `pg_isready` inside PostgreSQL; inspect valid JSON Lines and the required fields; inspect exposed network ports; verify that volumes survive `make down`; check Makefile commands.

**Unit tests:** **none mandatory**, because no business rule is introduced.

**Automated integration tests:** no additional automated-test project in Sprint 1. Integration checks are performed directly against the running services.

**Manual checks:** log readability, README reproducibility, useful failure messages, absence of secrets in command output, and non-destructive shutdown.

**Out of scope for testing:** business-entity persistence after restart, EF Core/migrations, authentication/authorization, graphical tests, AI providers, backup/restore, load and performance.

### Sprint 1 Traceability — To Be Regularized During Sprint 2

**Retrospective parent issue to create:**

- **Title:** `Sprint 1 — Solution and Infrastructure Foundation`.
- **Description:** `Track the initial .NET API, Docker Compose, PostgreSQL, structured logging, Makefile, and setup documentation. Retrospective record: Sprint 1 work predates the GitHub governance workflow. Verify AC01–AC10 against existing evidence; do not infer unverified success.`
- **Labels:** `type:sprint`, `area:infrastructure`, `status:retrospective`.
- **Planned versioned files:** `docs/Alycia-Sprint-1-SMART.md` (English version of the contract) and `docs/Alycia-Sprint-1-END.md` (retrospective report based on real evidence), to be added by the owner during Sprint 2.
- **Number / URL:** unknown until actually created.

**Planned deliverable sub-issues:**

| Exact title (English) | Expected description (English) | Labels | AC |
|---|---|---|---|
| `[S1] Establish .NET solution and API health endpoint` | `Verify the .NET 10 solution and Alycia.Api host build; verify the local /healthz HTTP response.` | `type:task`, `area:api`, `status:retrospective` | `AC01`, `AC04` |
| `[S1] Configure Docker Compose and PostgreSQL` | `Verify Compose configuration, API/database startup, local network exposure, PostgreSQL readiness, and volume retention.` | `type:task`, `area:infrastructure`, `status:retrospective` | `AC02`, `AC03`, `AC05`, `AC08` |
| `[S1] Configure structured and persistent logging` | `Verify Serilog JSON Lines output, trace and HTTP timing fields, persistence, rotation, and retention.` | `type:task`, `area:observability`, `status:retrospective` | `AC06`, `AC07` |
| `[S1] Document setup and Makefile commands` | `Verify the build/up/status/down commands, reproducible setup documentation, and safe handling of local secrets.` | `type:task`, `area:docs`, `status:retrospective` | `AC09`, `AC10` |

Once created, link the sub-issues to their parent and record their actual URLs and **genuinely available** evidence. If the work has already been merged or committed, **do not invent a historical PR, rewrite commits to add issue references, or backdate events**. Sub-issues with verified evidence may be closed after a retrospective check; an issue without sufficient evidence remains open. This regularization belongs to Sprint 2 and is not a reason to repeat Sprint 1.

**Exception for Sprint 1 boundary commits and signatures:** these conventions were adopted after Sprint 1 may have started, so they do not apply retroactively. The English SMART and END documents are to be added to the repository during Sprint 2 and linked to traceability records. Existing unsigned historical commits remain unchanged; `git commit -S` applies to **new** commits beginning with Sprint 2.

### End-of-Sprint Report to Be Regularized

**Language and naming requirement:** before its retrospective addition to the repository during Sprint 2, the approved French source must be translated completely and faithfully into English, preserving its meaning, ten `ACxx`, scope and issue plan. The versioned document must be named exactly `docs/Alycia-Sprint-1-SMART.md`; the retrospective report `docs/Alycia-Sprint-1-END.md` must be written in English and based only on real evidence. No initial commit or signature is simulated for this historical sprint.

`docs/Alycia-Sprint-1-END.md` is a **retrospective** Sprint 1 report, written in English and based on check outputs the owner actually has or executes. It presents AC01–AC10, evidence or missing evidence, Git and service status, limitations, and active work time only if it was actually recorded. It invents neither initial/final Git boundaries, nor an earlier issue or PR, nor a retroactively signed commit. Creating it during Sprint 2 is recorded as a Sprint 2 governance action, not backdated to Sprint 1.

### Implementation Steps

1. Check the local repository and technical prerequisites; do not block on the absence of `origin` or GitHub issues.
2. Create the solution and `Alycia.Api` on the existing or approved local working branch.
3. Configure Serilog and `/healthz`; add Dockerfile, Compose, PostgreSQL and volumes.
4. Add the Makefile, local configuration and documentation.
5. Execute the ten checks and record observed evidence.
6. During Sprint 2, regularize traceability: English sheets, issues and a PR if still technically possible, without fabricating dates or rewriting history.

### Closure Condition

The **ten acceptance criteria must be verified on the actual environment**. A check not executed is not considered successful; no blocking failure is ignored. **Administrative GitHub regularization is transferred to Sprint 2.** Technical acceptance of Sprint 1 depends on evidence for AC01–AC10, not on retroactively creating issues or PRs. If that evidence is missing, do not treat the sprint as closed; never invent a result.

**MVP progress after this sprint, if validated:** still `0/8` business capabilities completed.
