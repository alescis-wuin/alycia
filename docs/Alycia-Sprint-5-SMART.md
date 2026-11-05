# Alycia — Sprint 5: SMART Specification

**Sprint:** Sprint 5 — API Contracts and Developer Diagnostics
**Status:** **APPROVED — D1 and D2 approved on November 5, 2026; implementation and acceptance checks pending**
**Type:** focused technical foundation, with no newly completed MVP capability
**Authoritative source:** the approved French SMART sheet; this English translation has been rechecked following D2 approval.
**Official repository:** https://github.com/alescis-wuin/alycia (the AI's GitHub connector is read-only).
**Working branch:** `chore/api-contracts-developer-diagnostics`, created from `develop` after verifying the local state.
**PR target:** `develop`. **Published version:** none. **MVP progress:** 0/8 completed capabilities.
**References:** Charter R1.12; Consolidated Scoping R15; `C08.P01` (application JSON envelope), `C08.P12` (local observability), `C08.P13` (local tests), `C08.P17` (HTTP contracts still deferred beyond `/healthz`), `C08.P18` (tools as needed), `C04.P07` (secondary libraries).

> **Separate scope decisions — APPROVED on November 5, 2026:** the **S4–S9 schedule was already approved**. **D1** separately approved the targeted exception allowing Sprint 5 to be a standalone technical foundation; **D2** separately approved the complete French Sprint 5 SMART. Neither approval implicitly selects packages, authorizes business endpoints, or settles other deferred technical choices.

## 1. SMART Objective

**Objective:** make Alycia's actually exposed HTTP contracts available through generated OpenAPI JSON and a local development-only Scalar reference, and provide useful request diagnostics from existing Serilog events through focused search and JSON export, without an observability server or full request storage.

- **Specific:** generate contracts for existing routes, show Scalar only in `Development`, make one real `/healthz` request traceable and export its existing events.
- **Measurable:** valid JSON document at `/openapi/v1.json`, functional `/scalar` UI in development, both routes absent in production, accurate `/healthz` contract, reproducible event filtering/export, and recorded targeted checks.
- **Achievable:** `.NET 10`, `Alycia.Api`, `/healthz`, Serilog, JSON Lines, `make check`, and xUnit tests exist on `develop` after Sprint 4. OpenAPI/Scalar packages remain **PROPOSED** implementation choices; compatible versions will be checked at execution time.
- **Relevant:** inspect API contracts and simplify diagnosis before the first persistent business slice planned for Sprint 6.
- **Time-bounded:** **4 to 6 indicative sessions of 45–90 minutes** (estimated 3–9 active hours, not guaranteed). Continue the same sprint if criteria are unmet; commit-to-commit intervals measure calendar time, not actual work.

**Final demonstration:** a local API started in `Development` serves real-route OpenAPI JSON and Scalar; a `Production` launch exposes neither resource; one `/healthz` call can be tied to its existing structured events and exported without sensitive data.

## 2. Initial State, Prerequisites and Decisions

- **GitHub verified November 5, 2026:** `develop` at `0c786948a9b6656614ab0248b705cf8d2144d7bb`; [PR #26](https://github.com/alescis-wuin/alycia/pull/26) merged and issues #21–#25 closed. Sprints 1–4 are closed; `main` and `testing` have not been promoted, and no version has been published. Recheck this snapshot at sprint start.
- **Integrated code:** `Alycia.Api`, `Alycia.Client.Avalonia`, one .NET 10 xUnit project; Makefile `build`, `up`, `status`, `down`, `format`, `lint`, `test`, `check`, `install-hooks`; `.editorconfig`, optional Git hook; console/JSON Lines Serilog, `TraceId` and HTTP duration. No OpenAPI or Scalar is visible on `develop` in the preparatory read.
- **Approved `/healthz` contract:** technical local GET, unauthenticated, outside the business JSON envelope and without a PostgreSQL check; HTTP 200 with exact plain-text body `Healthy` when healthy, 503 when health checks fail. PostgreSQL is checked separately.
- **Local prerequisites:** clean worktree or safely preserved changes, up-to-date `develop`, .NET 10 SDK, Bash/Make, Git and GPG/SSH signing, and a local environment able to run the API on loopback. Check JSON reading tools (for example `jq`) before choosing commands that require them; do not impose those on the server.
- **Decision D1 — APPROVED (November 5, 2026):** the **Sprint 5 exception is narrowly limited to API contracts and local diagnostics**; it does not authorize additional standalone foundation sprints beyond the historical S1–S4 exception.
- **Decision D2 — APPROVED (November 5, 2026):** the complete French Sprint 5 SMART (objectives, scope, AC01–AC09, deliverables and issues) was approved separately from D1. The English translation was then rechecked for fidelity.
- **No presumed technology choice:** minimal proposal `Microsoft.AspNetCore.OpenApi` plus `Scalar.AspNetCore`, with stable .NET 10-compatible versions; do not install before D1/D2. xUnit already exists, without implying new artificial tests are required.

## 3. Included Scope

1. Add **native ASP.NET Core OpenAPI generation** to the API with the default `v1` document in JSON. Describe only endpoints actually exposed; do not invent a business API or DTO to populate the documentation.
2. Ensure **accurate documentation of `GET /healthz`** in OpenAPI: method, path, successful `200` response as `text/plain` (`Healthy`), and unhealthy `503`, without changing its behavior, local exposure or out-of-envelope status. Add only the endpoint metadata strictly needed if the generator does not include it correctly.
3. Propose **Scalar.AspNetCore** as the interactive reference at `/scalar`, backed by `/openapi/v1.json`, only when `app.Environment.IsDevelopment()` is true. No external account, hosted Scalar, agent or SaaS function is needed; do not enable additional external connections.
4. Keep both documentation routes unavailable in `Production` (expected 404) while preserving the approved `GET /healthz` behavior. Do not switch the production Docker Compose environment to Development or expose another host port.
5. Document a local procedure to **trace one actual HTTP request using `TraceId`** in existing Serilog events, including method, path, status and duration. Reuse the persistent JSON Lines and actually available fields; do not capture sensitive request headers or bodies.
6. Document **focused search/filter/export** of existing JSON Lines events for one or several selected requests, producing a temporary JSON Lines file and optionally a standard JSON array. Use simple safe CLI commands (`jq` only if available); no request database, custom explorer or new Docker service.
7. Update English documentation covering local loopback API launch in `Development`, OpenAPI/Scalar addresses, OpenAPI JSON export, `Production` behavior, trace correlation and log export, confidentiality constraints, targeted checks and temporary output file hygiene.
8. Produce an approved-source English SMART and factual English pre-PR END in distinct signed commits, with the usual PR and GitHub traceability.

## 4. Excluded Scope

- New business endpoint, application database, `DbContext`, migration, authentication/authorization, modification of the shared JSON envelope or `/healthz`.
- Automatic full request/response logging, bodies, attachments, authorization headers, API keys, prompts or document content.
- Custom Web request-search/sort/filter UI, PostgreSQL audit store, dashboard, Seq/ELK, centralized collection or unredacted mass exports.
- Distributed OpenTelemetry, new metrics, continuous profiling, benchmarks, monitoring, CI/CD, GitHub Actions or an extra Docker service.
- Public Scalar/OpenAPI hosting, validating a business API through a fake route, demo endpoints or review of authentication security not yet implemented.
- Additional Avalonia/MAUI features, Windows tests, software release or promotion to `testing`/`main`.

## 5. Deliverables

| Deliverable | Expected outcome | Proposed location |
|---|---|---|
| D1 — Real OpenAPI contract | Generated `v1` document, correct `GET /healthz`, valid JSON export | `src/Alycia.Api/Program.cs`, `Alycia.Api.csproj`, justified metadata |
| D2 — Scalar reference | Interactive reference displaying the actual generated OpenAPI, limited to Development | API and minimal configuration |
| D3 — Local diagnostics | Trace search and focused JSONL/JSON exports without new collection | README or focused English doc; minimal script only if indispensable |
| D4 — Documentation and checks | Local startup, production unavailability, log hygiene and targeted checks | `README.md` or a focused doc |
| D5 — Sprint 5 traceability | Reviewed English SMART, factual English END and issue/AC-linked PR | `docs/Alycia-Sprint-5-SMART.md`, `docs/Alycia-Sprint-5-END.md`, GitHub |

## 6. Acceptance Criteria and Expected Evidence

| ID | Required observable outcome | Expected verification and evidence |
|---|---|---|
| **AC01** | OpenAPI generation describes **at least actual `GET /healthz`** with its real method/path, successful `200` as `text/plain` and unhealthy `503`, without inventing business routes or changing the health contract. | Inspect `/openapi/v1.json` in Development and verify operation/responses; inspect the `/healthz` mapping and compare with a real `curl` 200 + `Healthy`. Not reproducing a live 503 is not a failure when only the healthy case is feasible. |
| **AC02** | `/openapi/v1.json` in Development returns a **valid, retrievable OpenAPI JSON document** generated from registered endpoints, without secrets; saving that document locally is reproducible. | Local `curl`, JSON parsing (available tool or explicit alternative), check `openapi`, `paths` and methods; save outside the repository in a temporary file. |
| **AC03** | `/scalar` in Development genuinely displays an interactive reference that loads **the same document** and presents `/healthz` without loading errors. | Start local API in Development, really open the UI and observe loading or obtain precise owner observations; an HTTP 200 for the HTML page alone is insufficient. |
| **AC04** | In `Production`, `/scalar` and `/openapi/v1.json` are not exposed (expected 404); `/healthz` continues to return `200` and `Healthy` when healthy. No extra Docker host port or implicit Production-to-Development switch. | Run local API separately in Development and Production on a test port; `curl -i` the three routes and inspect project and Compose configuration. |
| **AC05** | One real `/healthz` request can be found in **existing logs** by `TraceId`, showing method, path, status and duration, without new sensitive capture. | Send a local request, read JSON Lines and console events, check the observed correlation ID, `RequestPath`, `StatusCode` and `Elapsed`; never invent a trace. |
| **AC06** | A documented CLI procedure can **filter** one or more requests by ID and **export their structured events** as valid JSON Lines, with optional JSON-array conversion if a suitable optional tool exists, without editing source log files. | Execute a filter on a sample of existing logs, verify event count, parse at least one JSON line, confirm source files are unchanged and temporary exports are excluded from commits. |
| **AC07** | Diagnostics and documentation expose no request bodies, secrets, tokens or sensitive headers; existing Serilog storage/rolling, `/healthz` behavior and network restrictions are preserved. | Focused diff/configuration and redacted sample inspection; no new capture middleware, Docker service or sensitive data output. Do not claim absolute security from one sample. |
| **AC08** | English documentation reproduces Scalar launch, OpenAPI JSON export, Development/Production checks, JSONL search/export and existing quality commands without claiming a business API exists. Relevant build and checks pass. | Review README and execute essential flows; `make check` and targeted API build only as warranted by changes, avoiding redundant tests. |
| **AC09** | The French SMART is approved and faithfully translated before the **first signed SMART-only commit** containing `docs/Alycia-Sprint-5-SMART.md`; factual English END is committed separately and signed **before PR opening**; **after opening and before merging**, check PR into `develop`, real SMART/END/issue links, one checkbox per AC01–AC09, supporting evidence and GitHub `Verified` signatures. **AC09 passes in full only after PR review.** | **Before PR:** history, local signatures, docs and END, with PR review `PENDING`; **after opening:** target, links, checklist, evidence and GitHub signatures; issue closure only after merging. |

**No Sprint 5 AC has yet been demonstrated.** The owner executes commands on CachyOS; the AI never claims local checks without supporting evidence. AC09 remains `PENDING` in the future pre-PR END even if its earlier documentary requirements have been met.

## 7. GitHub Issue Plan — Owner-Created and Linked

**Parent issue:**
- **Exact title:** `Sprint 5 — API Contracts and Developer Diagnostics`
- **English description:** `Generate a truthful local OpenAPI v1 document and Scalar reference for the actual Alycia API routes, expose documentation only in Development, enable safe trace-based inspection and targeted JSON exports of existing Serilog events, and verify the workflow without business endpoints, new storage, CI/CD or sensitive request capture. Track AC01–AC09 through the approved SMART and factual END report.`
- **Labels applied:** `type:sprint`, `area:api`.
- **SMART link:** actual URL for `docs/Alycia-Sprint-5-SMART.md` only after publishing the branch.
- **Number/URL:** [#27](https://github.com/alescis-wuin/alycia/issues/27).

| Exact sub-issue title | English description | Proposed labels | Covered AC | Real dependency | Number/URL |
|---|---|---|---|---|---|
| `[S5] Generate accurate OpenAPI contract and JSON export` | `Add native OpenAPI generation for actual routes, describe GET /healthz truthfully with 200 text/plain and 503, and demonstrate valid JSON export without new business endpoints.` | `type:task`, `area:api` | AC01–AC02 | Parent; existing API | [#28](https://github.com/alescis-wuin/alycia/issues/28) |
| `[S5] Add development-only Scalar API reference` | `Render the generated OpenAPI document in Scalar in Development, confirm the real health operation, and prevent documentation routes from appearing in Production.` | `type:task`, `area:api` | AC03–AC04 | Parent; OpenAPI document | [#29](https://github.com/alescis-wuin/alycia/issues/29) |
| `[S5] Document trace inspection and safe JSON event exports` | `Trace a real health request through existing Serilog events, filter and export selected JSON Lines safely, and preserve sensitive-data and logging boundaries.` | `type:task`, `area:observability` | AC05–AC07 | Parent; existing events | [#30](https://github.com/alescis-wuin/alycia/issues/30) |
| `[S5] Verify developer workflow and sprint evidence` | `Document local development diagnostics and reproduction commands, gather observed build and runtime evidence, prepare signed English END and check the PR acceptance list.` | `type:task`, `area:docs` | AC08–AC09 | Parent; deliverables ready | [#31](https://github.com/alescis-wuin/alycia/issues/31) |

The owner created and linked all four sub-issues (#28–#31) to parent #27 through native GitHub sub-issue relationships. The AI performs read-only GitHub checks and does not create or modify issues.

## 8. Implementation Steps and Indicative Effort

| Session | Steps and intermediate outcome |
|---|---|
| **1 — 45–90 min** | Verify `develop` HEAD, S4 closure and locally available tools; **obtain decision D1 on the S5 exception**, then **D2 approval of the French SMART**. Compare EN/FR section by section; create branch and first signed SMART-only commit. Prepare/create issues if authorized. |
| **2 — 45–90 min** | Integrate OpenAPI JSON, accurate `/healthz` metadata, and verify generated contract and local export without new business routes. |
| **3 — 45–90 min** | Enable Scalar only in Development; actually check document loading, Production 404 responses and preserved `/healthz`. |
| **4 — 45–90 min** | Verify correlation in existing logs, focused JSONL/JSON filtering/export and confidentiality boundaries. |
| **5 — 45–90 min** | Complete README and only the necessary targeted checks; diagnose failures and stop after success. |
| **6 — if required** | Prepare factual English pre-PR END, separate signed commit; open PR, verify AC01–AC09, links and signatures before merging; close satisfied issues after merge. |

Effort is indicative; keep Sprint 5 open until its mandatory criteria and prerequisite decisions are satisfied.

## 9. Targeted Verification

**Unit tests:** no new test project is mandated. Reuse existing xUnit tests when changes touch their responsibilities; add focused unit tests only for genuinely testable new logic, without simulating a nonexistent business API.

**Essential integration:** run the local API in Development then Production; verify OpenAPI JSON/Scalar/404, actual `/healthz`, Serilog events and selected structured sample export; verify build and `make check` after relevant changes.

**Manual checks:** visually open Scalar; inspect OpenAPI JSON, local event filter/export, sensitive-data boundaries, new routes and environment restrictions.

**Explicitly excluded checks:** MAUI/Windows, authentication, PostgreSQL/migrations, load/performance, distributed telemetry, custom request Web system, external-service tests, CI/CD and releases. Do not exclude any check required by an AC.

**Evidence policy:** actual owner-supplied outputs only; redacted logs and screenshots, non-destructive read-only checks, no duplicate rerun after success without changes or concrete doubt. GitHub is accessed read-only by the AI.

## 10. Start and Completion Conditions

**Before any commit/implementation:** explicit favorable decision on the **targeted S5 foundation exception**, explicit approval of the **complete French SMART**, followed by full English translation equivalence. Without either decision, do not start the sprint.

**Before PR:** AC01–AC08 evidenced, pre-PR documentary portion of AC09 verified; factual English END in its own signed commit. This report leaves **AC09 `PENDING`** for the future PR review.

**After opening and before merging:** verify `develop` target, actual SMART/END/issue links, nine-AC checklist, evidence, `Verified` signatures and AC09 conditions. AC09 is `PASS` only after this review. Do not merge with any mandatory AC `PENDING`.

**After merge:** manually close satisfied sub-issues then the parent without retrospectively editing the pre-PR END. **MVP:** no additional completed capability from this technical sprint alone; no version release.

**Status of this sheet: APPROVED — D1 and D2 explicitly approved on November 5, 2026; acceptance evidence pending.**
