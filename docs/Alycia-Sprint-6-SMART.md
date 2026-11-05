# Alycia — Sprint 6: SMART Specification

**Sprint:** Sprint 6 — Domain to PostgreSQL Vertical Slice
**Status: APPROVED — the owner explicitly approved the complete French SMART and this English translation on November 5, 2026; implementation not started.**
**Type:** first persistent vertical business slice, without prematurely completing an MVP capability.
**Authoritative source:** the approved French SMART; this owner-approved English translation has been reviewed for documentary equivalence in this kit before any first commit.
**Official repository:** https://github.com/alescis-wuin/alycia; AI access is read-only.
**Proposed branch:** `feature/domain-postgresql-vertical-slice`, created from the latest `develop` after S5.
**PR target:** `develop`. **Published version:** none at the latest review. **Completed MVP capabilities:** 0/8 at the latest review.
**References:** Charter R1.12; Consolidated Scoping R15; `C05.P01`–`C05.P10` (architecture/modules), `C05.P11` (S6 DbContext/migration specifics approved here; historical decision register unchanged), `C08.P01`–`C08.P02` (JSON contract), `C08.P04` (network security), `C08.P14` (identity: DEFERRED to S7–S9), `C08.P16` (targeted S6 persistence choices approved here), `C09.P04` (end-to-end evidence).

> **Decision boundaries:** the S4–S9 schedule is **APPROVED**; D1 (`Workspace`), D2 (strictly local development-only security exception) and D3 (repository, EF Core and migrations) were **explicitly APPROVED by the owner in the preparatory conversation on November 5, 2026**. The complete S6 SMART (D4), **including AC01–AC10, deliverables, exclusions, test plan and four sub-issues, is APPROVED by the owner on November 5, 2026**, as is its English translation. Approval does not mean sprint startup or fulfilled acceptance criteria. **S5 need not finish before S6 preparation**, but its final integrated state must be reviewed before **starting** S6. Only one sprint may be implemented at a time. D2 is a one-sprint exception to the general Charter rule, not a permanent security policy.

## 1. SMART Objective

**Objective:** deliver a **real, verifiable first business slice** to create and retrieve a `Workspace` in Avalonia, through `Workspaces.Application` use cases, a dedicated repository and `WorkspacesDbContext` into PostgreSQL, recover it after restart, and provide explicit errors, correlated diagnostics, targeted tests and anonymous endpoints restricted to local development under D2.

- **Specific:** the `Workspace` entity owned by `Workspaces`, already approved fields and invariants (identity, name, creation timestamp and case-insensitive uniqueness), create/read through Domain, Application, Infrastructure repository, API, Avalonia and PostgreSQL.
- **Measurable:** explicit, repeatable `make migrate`, PostgreSQL migration, data read after restart, error cases, tested `Development`/opt-in/loopback/production access restrictions, HTTP contract, UI flow and actually executed tests.
- **Achievable:** the .NET 10/API, PostgreSQL/Compose, Avalonia, Serilog and xUnit foundation was integrated by S4; review S5 before starting. Create modular projects only when they are used.
- **Relevant:** prove an actual persistent application chain before multiplying modules and features.
- **Time-bounded:** **5 to 8 indicative sessions of 45–90 minutes**, continuing the same sprint until criteria pass. This estimate does not authorize security shortcuts or fictional closure.

**Observable result:** in an explicitly enabled `Development` environment effectively restricted to local access, Avalonia creates and retrieves a persistent `Workspace` after API restart and shows loading, success, validation, unavailable and disabled/denied-access states. No anonymous business endpoint is available in production.

## 2. Initial State, Prerequisites and Decisions

- **Last remote evidence at preparation time (November 5, 2026):** `develop` at `0c786948a9b6656614ab0248b705cf8d2144d7bb`, S4 [PR #26](https://github.com/alescis-wuin/alycia/pull/26) merged, issues #21–#25 closed. Two application projects (`Alycia.Api`, `Alycia.Client.Avalonia`) and one xUnit test project exist. No business Domain/Application/Infrastructure project or migration was observed. **This does not establish the state after S5.**
- **S5 transition:** Sprint 5 — API Contracts and Developer Diagnostics was **PROPOSED** in its earlier kit; its foundation exception and SMART required approval. Before S6 execution, inspect any approved S5 SMART, END, PR and the changes actually merged into `develop`. Do not assume Scalar, OpenAPI or a new application contract already exists.
- **D1 — APPROVED:** the first business entity is **`Workspace`**, owned exclusively by the `Workspaces` module. S6 flow: create and retrieve (list only if needed for the Avalonia path). **Already APPROVED:** `Id` (`Guid`), `Name` (`string`), `CreatedAtUtc` (`DateTimeOffset`), trimmed required name (maximum 100 characters), immutable ID and case-insensitive unique name. The HTTP behavior specified by the approved SMART is approved; exact URL paths and implementation details consistent with its criteria remain to be selected. No disposable model.
- **D2 — APPROVED: targeted S6 security exception.** Anonymous `Workspace` business endpoints are permitted **only** in `Development`, behind a **separate explicit opt-in** (disabled by default), for **actual local-machine-only** use with nonsensitive development data. No anonymous business route in `Production` even when the opt-in flag is present, and no LAN/VPN exposure. Verify actual networking, including traffic between Docker containers: host port publishing on `127.0.0.1` and the `Development` environment name alone do not guarantee protection from the Docker network. Reject access not actually local, without trusting unverified `X-Forwarded-For` headers; select a local run mode that can enforce these requirements. Test denial in Development without opt-in, outside loopback, and in Production; preserve technical `/healthz`. **This temporary exception applies only to S6, is never automatically extended and is not production-grade authentication.** Identity sprints S7–S9 must implement real resource protection; S6 endpoints cannot be enabled in production before effective authentication.
- **D3 — APPROVED:** `WorkspacesDbContext` resides in `Alycia.Modules.Workspaces.Infrastructure`; migrations are owned by that Infrastructure and **explicitly applied**, never run automatically on API startup. A **dedicated `Workspace` repository is mandatory**, its interface in `Workspaces.Application`, EF Core implementation in `Workspaces.Infrastructure`. Application services never manipulate `DbContext` or SQL; Domain owns invariants. Simple atomic writes use standard EF Core `SaveChanges` transactions; no generic transaction manager. A **single central `make migrate` command** is the entry point for all required module migrations: S6 applies only Workspaces; future modules must be registered explicitly in a deterministic order based on real dependencies. **PROPOSED implementation detail:** distinct, identifiable migration history per context once multiple contexts exist; exact physical table/schema names are not yet approved. Exit on failure, never report partial success as a global success. **Deterministic execution is not globally atomic** across module migrations; document partial-failure recovery. The exact SQL schema and the technical implementation of name uniqueness are implementation details; **a PostgreSQL case-insensitive unique constraint is already an approved requirement**. Do not predetermine future modules.
- **D4 — APPROVED:** the owner explicitly approved the **complete French S6 SMART** and its English translation on November 5, 2026 (AC01–AC10, scope, exclusions, deliverables, tests and issue plan). Do not request approval of D1–D4 again. **Before the first signed commit**, verify the English file to be committed matches the approved text and actual S5 changes do not require substantive revisions. Material changes require explicit reapproval; implementation details that preserve the approved ACs may be settled during S6.
- **Practical prerequisites:** locally clean `develop` or preserved changes, operational GPG/SSH signing, .NET 10, Docker Compose/PostgreSQL, Avalonia on CachyOS, `make check`, and an integration-test environment using isolated or identifiable test data.


### `Workspace` Properties and Invariants — APPROVED by the Owner (D1)

| Property | C# type | Approved rule |
|---|---|---|
| `Id` | `Guid` | Unique, nonempty identifier (`Guid.Empty` forbidden), backend-generated and immutable after creation. |
| `Name` | `string` | Required, nonblank after trimming leading/trailing whitespace; **maximum 100 characters after trimming**. |
| `CreatedAtUtc` | `DateTimeOffset` | Creation timestamp assigned by the backend in **UTC**. |

- **APPROVED uniqueness:** two Workspaces cannot share the same name **case-insensitively**, after trimming leading/trailing whitespace. `Personal`, `personal` and ` Personal ` therefore count as the same business name for duplicate detection. The uniqueness rule applies to Workspaces in the single-owner instance and does not preselect the future authentication mechanism.
- **Responsibilities:** Domain enforces intrinsic invariants (`Id`, `Name`, creation timestamp); Application checks name existence through `IWorkspaceRepository`; **PostgreSQL also enforces a case-insensitive unique constraint/index** consistent with the lookup comparison. The database constraint handles concurrent creates; a duplicate detected before or during `SaveChanges` returns **HTTP `409 Conflict`** in the application JSON envelope and does not create a second row.
- **HTTP behavior approved under D4:** create with a name → **201** and identifier; read by identifier → **200** or **404**; invalid input (empty, whitespace-only, over 100 characters) → **400**; case-insensitive duplicate → **409** (**conflict status approved**). Exact URL paths and technical details not fixed by the ACs must be decided consistently with the API actually integrated by S5.
- **Outside this slice:** rename, delete, RAG preferences, projects, multi-user authorization and any Unicode/collation convention not required for the approved behavior. Application comparison and PostgreSQL uniqueness must be consistent, with tests for the approved examples; do not invent additional normalization policies.

## 3. Included Scope — APPROVED

1. Define `Workspace` in `Alycia.Modules.Workspaces.Domain` with `Id: Guid`, `Name: string`, `CreatedAtUtc: DateTimeOffset` and already approved invariants (trimmed required name, 100-character limit, case-insensitive unique name, valid immutable ID). Create only necessary `Workspaces.Domain`, `Workspaces.Application` and `Workspaces.Infrastructure` projects, not the five other modules.
2. Implement `WorkspacesDbContext` and `EfWorkspaceRepository` in `Alycia.Modules.Workspaces.Infrastructure`, their PostgreSQL mappings, **case-insensitive unique name constraint**, other required storage constraints and Infrastructure-owned migrations. Provide **`make migrate`**, the single explicit command intended for all persistent modules, applying only Workspaces during S6; keep EF Core migration history inspectable and future intermodule order explicit. Never migrate automatically at startup or report success on failure. Preserve data after restart.
3. Implement `Workspaces.Application` **create** and **read** use cases (list only if needed), depending on the mandatory dedicated **`IWorkspaceRepository`** in Application rather than EF Core. Infrastructure implements persistence; transactions use standard `SaveChanges`. Handle validation, **name conflict `409` (including concurrent creates)**, absence and dependency failures.
4. Expose `Workspace` HTTP routes **only in `Development` with explicit opt-in and effective local-only access**; routes are absent or denied outside this regime and **absent in production**. Verify Docker networking in addition to `127.0.0.1` host port publishing. Use real HTTP statuses and JSON envelope `success`, `status`, `code`, `message`, `data`, `errors`, `traceId`, `timestamp`. Keep technical `GET /healthz` outside the envelope without a PostgreSQL check. Reuse OpenAPI/Scalar only if S5 actually integrated it.
5. Provide a minimal but **working** Avalonia create/read interaction with initial, loading, success, validation/denial/error and unavailable states; allow safe retry without accidental duplicate writes. Do not build a generic UI or speculative navigation system.
6. Prove the end-to-end flow against **real PostgreSQL**: create through Avalonia, retrieve after restarting the backend, and no ghost record after a rejected write. Isolate test data without deleting existing databases or volumes without approval.
7. Reuse Serilog and HTTP correlation for create/read operations; exclude secrets, passwords, sensitive content and personal data from events. Extend existing xUnit testing only with useful Domain/Application cases and a pertinent persistence integration test.
8. Document the model and its security status, migration, local execution, persistence verification, limitations and Git/END/PR workflow in English.

## 4. Excluded Scope

- Fake authentication, assumed production authorization, a temporary session presented as secure, or any anonymous business endpoint **outside the approved D2 exception**.
- Full Identity implementation, token refresh, secret vault, multiuser support, multiple modules and unnecessary full CRUD.
- Creating all six modules at once, a universal `Shared` layer, distributed architecture, **universal generic repository** (the specific Workspace repository is mandatory), or extra validation/caching/events without concrete need.
- Migrating existing real data, deleting volumes, publishing network services, exposing PostgreSQL to LAN/Internet, or invasive automatic startup migration.
- Redesigning `/healthz`, repeating S5 OpenAPI/Scalar work, distributed telemetry, benchmarks, monitoring, CI/CD, MAUI/Windows or software publication.
- Treating an explicitly authorized development-only demonstration as an MVP-complete capability without the required client parity and security.

## 5. Deliverables

| Deliverable | Expected outcome | Proposed location |
|---|---|---|
| D1 — Domain and use cases | `Workspace`, invariants, creation/retrieval through Application services and `IWorkspaceRepository` | `src/Alycia.Modules.Workspaces.Domain/`, `src/Alycia.Modules.Workspaces.Application/` |
| D2 — Persistence | `WorkspacesDbContext`, `EfWorkspaceRepository`, mappings, explicit migrations, `make migrate`, constraints | `src/Alycia.Modules.Workspaces.Infrastructure/`, `Makefile` and minimal support if needed |
| D3 — API and security | Actually authorized routes, JSON envelope, errors and correlation | `src/Alycia.Api/`, contracts if needed |
| D4 — Avalonia flow | Minimal screen/action, state transitions and retry | `src/Alycia.Client.Avalonia/` |
| D5 — Tests, evidence, docs | Domain/persistence tests, complete flow, README, SMART/END | `tests/`, `README.md`, `docs/Alycia-Sprint-6-*.md` |

## 6. Acceptance Criteria and Planned Evidence

| ID | Mandatory observable result | Expected check / evidence |
|---|---|---|
| **AC01** | `Workspace` is the sole entity in the slice and belongs to `Workspaces`; approved D1–D3 choices and already approved properties/invariants (`Guid`, trimmed unique case-insensitive name, maximum 100 characters, UTC creation timestamp) are respected without artificial project or endpoint. | D1–D4 decision traceability, solution inventory, Domain/Application/Infrastructure and contract review. |
| **AC02** | Domain rejects `Guid.Empty`, blank/whitespace-only names and names exceeding 100 characters after trimming, assigns `CreatedAtUtc` in UTC and keeps `Id` immutable, without UI/SQL dependencies. Case and surrounding whitespace variants count as duplicate business names. | Focused Domain/Application tests (blank names, 100/101 limit, trimming, ID/UTC invariants), dependency review; uniqueness and concurrency verified against real PostgreSQL. |
| **AC03** | `WorkspacesDbContext`, mappings, **PostgreSQL case-insensitive unique constraint** and migrations belong to Infrastructure; `make migrate` explicitly applies S6 Workspaces migrations, is repeatable without reapplying completed migrations, returns nonzero on failure and does not run migrations automatically at API startup. Data can be retrieved after restart. | Inspect EF Core configuration/history, run `make migrate` twice on a controlled test DB, verify SQL state and relevant nondestructive failure, retrieve after restart. |
| **AC04** | `Workspaces.Application` services create/retrieve via dedicated `IWorkspaceRepository`; `EfWorkspaceRepository` handles database access without EF Core in business services, and `SaveChanges` retains its standard atomicity. Not found (`404`), invalid (`400`), duplicate (`409`, including concurrent inserts) and technical failures are distinguished; the application precheck does not replace the DB constraint. | Reference/DI review, focused service and real repository tests, observed create/read/error results. |
| **AC05** | Anonymous `Workspace` routes operate only in `Development` **with explicit opt-in and effective local-only access**; disabled without opt-in, inaccessible outside loopback and absent in `Production` even if opt-in is set. Docker/container access paths are checked, real HTTP statuses (`201`, `200`, `400`, `404`, `409` as applicable) and the envelope are correct, and `/healthz` is unchanged. | Positive/negative HTTP requests in each configuration, nonlocal and Docker-network access attempts, JSON contract and `/healthz` checks. |
| **AC06** | Avalonia offers complete create/read with loading, success, **validation and duplicate `409` feedback**, denial/error and unavailable states, without UI blocking or accidental duplicates on retry. | Actual CachyOS demonstration, captures/observations and focused failure cases. |
| **AC07** | A record created through Avalonia is read from PostgreSQL after restarting the API; rejected writes (invalid or case-insensitive duplicate names) leave no unintended persistent record, including concurrent conflicts. | Real API/DB/client integration scenario, API restart, data check and failure evidence. |
| **AC08** | Operations correlate via `traceId`; failures are observable and examined logs disclose no secrets or sensitive contents. | Real redacted HTTP responses and Serilog events; focused logging review. |
| **AC09** | Justified tests and `make check` pass; English docs cover `make migrate` as the sole entry point, future intermodule ordering and limits, D2 exception, launch and flow restrictions without claiming authentication. | Actual local check outputs, Makefile/README and migration-state review, no CI/CD. |
| **AC10** | French SMART approved and faithfully translated before the **first signed SMART-English-only commit**; factual English END in a distinct signed commit **before PR**; PR into `develop` checked **after opening and before merging** (actual SMART/END/issue links, one checkbox per AC01–AC10, evidence and GitHub `Verified` signatures). **AC10 passes only after reviewing the PR.** | Before PR: docs, Git and local signatures; after opening: actual PR, links, checklist and remote signatures; after merge: manually close issues. |

**No AC has passed at preparation time.** D1–D4 and the English translation are approved; implementation evidence and future PR checks remain outstanding.

## 7. GitHub Issue Plan — Owner-Created

**Parent issue:** `Sprint 6 — Domain to PostgreSQL Vertical Slice`
**English description:** `Deliver a real Workspace create/read vertical slice from Avalonia through API, Workspaces Application services and IWorkspaceRepository, EF Core Infrastructure and PostgreSQL. Add WorkspacesDbContext, explicit migrations via one Makefile entry point, Development-only explicitly enabled local business routes, restart persistence, failure handling, trace correlation and evidence-based AC01–AC10. No anonymous production business routes or speculative modules.`
**Proposed labels:** `type:sprint`, `area:infrastructure` (verify).
**Real ID and URL:** fill in only after actual creation.

| Exact sub-issue title | English description | Proposed labels | Covered criteria | Actual dependency | Real number/URL |
|---|---|---|---|---|---|
| `[S6] Define Workspace domain and application repository contract` | `Implement Workspace with approved Guid Id, trimmed unique case-insensitive Name (max 100), UTC CreatedAtUtc and domain invariants; add create/read Application services and dedicated IWorkspaceRepository with focused tests, without EF Core in business services.` | `type:task`, `area:domain` | AC01–AC02, AC04 | Parent, D4 | Pending |
| `[S6] Implement Workspace persistence and centralized migration command` | `Implement WorkspacesDbContext, EfWorkspaceRepository and a PostgreSQL case-insensitive unique name constraint with concurrency-safe conflict handling; own EF migrations in Infrastructure, expose make migrate as the central entry point and verify idempotence, errors and restart persistence.` | `type:task`, `area:infrastructure` | AC03–AC04, AC07, AC09 | Parent, Application contract ready | Pending |
| `[S6] Expose Development-only local Workspace API` | `Expose create/read operations only with explicit Development opt-in and effective local-only access, including Docker-network denial and absent Production routes; return 409 for duplicate Workspace names and retain standard JSON envelope and trace correlation.` | `type:task`, `area:api` | AC05, AC08 | Parent, repository functioning | Pending |
| `[S6] Deliver Avalonia Workspace flow and sprint evidence` | `Implement working Avalonia Workspace create/read with loading and failure states, prove PostgreSQL persistence and safe retry, document security/migration controls, and provide signed SMART/END and PR evidence.` | `type:task`, `area:client`, `area:docs` | AC06–AC07, AC09–AC10 | Parent, API available | Pending |

**Coverage:** every AC01–AC10 belongs to at least one sub-issue. Verify labels, real issue numbers and dependencies on GitHub. Do not create issues until the scope and decisions are approved.

## 8. Steps and Indicative Effort

| Session | Implementation and intermediate evidence |
|---|---|
| **1 — 45–90 min** | Review S5 and `develop`, confirm closure or blocker; **do not reopen approved D1–D4**. Confirm the English file to be committed matches the approved SMART; prepare branch, real issues and first signed SMART-only commit only after verified S5 closure. |
| **2 — 45–90 min** | Domain model and invariants; focused unit tests of approved rules. |
| **3 — 45–90 min** | Dedicated Application services/`IWorkspaceRepository`, `EfWorkspaceRepository`, `WorkspacesDbContext`, real PostgreSQL migration via `make migrate`; idempotence and relevant error checks. |
| **4 — 45–90 min** | D2 Workspace HTTP routes strictly Development/explicit opt-in/local, envelope and correlation; negative checks for Production, missing opt-in and Docker network. |
| **5 — 45–90 min** | Complete Avalonia flow, states and failures; create/read demonstration. |
| **6 — 45–90 min** | Restart recovery, no write on failure, logs and real integration. |
| **7 — 45–90 min** | Docs, `make check`, relevant tests, factual English END in a separate signed commit before PR. |
| **8 — if needed** | PR to `develop`, final AC10 verification, then owner closes issues after merge. |

Duration is indicative; do not close a sprint with a missing promised behavior or proof. Combine phases when one successful check covers several ACs.

## 9. Targeted Verification Strategy

**Unit tests:** already approved Workspace invariants (`Guid`, trimmed/nonblank/100-character `Name`, UTC `CreatedAtUtc`) and nontrivial use-case outcomes via `IWorkspaceRepository`; reuse xUnit, adding test projects only if useful. Avoid tests mirroring implementation.

**Essential integration tests:** `make migrate` on real PostgreSQL, repeat without reapplication and relevant failure diagnosis; real EF Core repository, case-insensitive uniqueness constraint and concurrent insert conflict (`409`), create/read after restart; HTTP success/validation/error and refusal in Development without opt-in, outside loopback, in Production and from Docker; actual Linux Avalonia flow. Isolate test data and preserve volumes.

**Manual checks:** UI states, retry, Application/repository/Infrastructure boundaries, `make migrate` configuration, D2 restrictions including container networking, dependency failures, redacted traces and docs. **No destructive tests** on a working database without approval.

**Out of scope:** load/benchmarks, CI/CD, future module tests, MAUI/Windows, unapproved full authentication, global data deletion, production migration, detailed retesting of all prior sprints.

**Evidence policy:** only the owner executes on CachyOS, supplies outputs and signs commits; the AI reads the official GitHub repository without writing. Stop when the relevant checks pass. On failure, diagnose and rerun only affected checks.

## 10. Start, Completion and Progress Rules

**Preparation now:** this kit and compatibility review of already approved D1–D4 against S5 can proceed during S5, with no S6 commit and without interrupting S5.

**Before S6 implementation:** review the final integrated S5 state on `develop`; **D1–D4 and both FR/EN SMART sheets are already approved**. Verify that the English document in the first commit matches the approved text and that completed S5 work does not require material changes; finalize only implementation details consistent with the ACs. Substantive revisions require fresh owner approval. Then create the branch and commit only the SMART in the first signed commit.

**Before opening PR:** AC01–AC09 actually demonstrated, documentary pre-PR part of AC10 evidenced, factual English END separately signed. In END, keep AC10 `PENDING` until its PR-related checks can occur.

**After opening, before merge:** verify PR into `develop`, real links, AC01–AC10 checklist, evidence and `Verified` signatures. Mark AC10 PASS only after verification. **After merge:** owner manually closes completed sub-issues, then parent.

**MVP:** the sprint does not complete any of the eight capabilities without all real criteria, including applicable security and MAUI parity. No software version or branch promotion automatically occurs.

**Final sheet status: APPROVED — D1, D2, D3 and D4 approved by owner on November 5, 2026; S6 implementation NOT STARTED and S5 state to be verified before startup.**
