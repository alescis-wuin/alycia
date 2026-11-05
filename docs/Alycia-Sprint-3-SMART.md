# Alycia — Sprint 3: SMART Specification

- **Sprint:** Sprint 3 — Avalonia Backend Status
- **Document revision:** 1 — approved on November 5, 2026
- **Status:** **APPROVED — not started**
- **Authoritative source:** the French SMART sheet in the startup archive. Any substantive change requires renewed approval before the English file is committed.
- **Type:** technical/UI foundation (approved exception for Sprints 1 through 4)
- **MVP capability completed by this sprint:** none (0/8)
- **Repository:** https://github.com/alescis-wuin/alycia
- **Approved working branch:** `feature/avalonia-backend-status`, derived from `develop`
- **PR target:** `develop`
- **Published software version:** none
**References:** Alycia Charter revision 1.9; Consolidated Scoping revision 12; technical contract `C08.P17`, graphical flow `C02.P08` (specified and approved for this sprint in this sheet).

## 1. SMART Objective

**Objective:** introduce a .NET 10 desktop Avalonia client runnable on CachyOS/Linux, able to query the existing technical `GET /healthz` endpoint asynchronously, clearly display checking, available or unavailable states, and repeat the check without restarting the application.

- **Specific:** one `Alycia.Client.Avalonia` client project and a minimal API status screen, without business functionality.
- **Measurable:** successful build, actual window launch on Linux and manual verification with backend running, stopped, then restarted; evidence from the owner's outputs and observations.
- **Achievable:** the ASP.NET Core 10 backend and `/healthz` already exist; Avalonia desktop supports Linux, subject to an operational graphical session.
- **Relevant:** establish the first graphical client-to-API flow without anticipating authentication or business persistence.
- **Time-bounded:** focused sessions of about 30–90 minutes as a guideline; continue the same sprint if acceptance criteria remain unmet.

**Observable outcome:** an Avalonia window opens on Linux, displays "checking" during the request, reports the API available only for HTTP 200 with the `Healthy` text response, clearly reports other outcomes, and correctly updates state when rechecked.

## 2. Initial State and Prerequisites

- **Observed remote state:** `develop` at `eeed2a82330d1a8031b44d630bb276bda7190ecb` in the latest audit; one `Alycia.Api` project in `Alycia.slnx`, no Avalonia client. PRs #12 and #14 merged; issues #1–#11 and #13 closed.
- **Local state:** must be confirmed using read-only commands on the owner's machine; do not presume a clean worktree, current Git configuration, or running services.
- **Technologies:** .NET SDK 10, Avalonia desktop and a working Linux graphical session. Check template availability and compatible packages during the sprint; select a suitable stable version without speculative dependencies.
- **Graphical session:** verify a usable Linux display. X11 or XWayland is sufficient; do not require a native Wayland backend without demonstrated need.
- **APPROVED backend contract:** local `GET http://127.0.0.1:8080/healthz` with the current backend configuration. A successful check returns HTTP `200` and plain text `Healthy`; a failed ASP.NET Core health check returns HTTP `503`. This endpoint does not check PostgreSQL and does not use the business JSON response envelope.
- **Git:** verified `develop`, a sprint branch derived from `develop`, functional `git commit -S` and GitHub "Verified" checks after publication.
- **Prerequisite decision:** French SMART sheet approved on November 5, 2026; any major change to the UI or flow requires another decision. The product's UI language remains deferred (`C02.P07`); temporary technical labels do not decide localization.

## 3. Included Scope

1. Create only the desktop project `src/Alycia.Client.Avalonia/Alycia.Client.Avalonia.csproj`, targeting `net10.0`, and add it to `Alycia.slnx`; no unnecessary shared subproject.
2. Implement an **asynchronous**, time-bounded HTTP check of the technical `GET /healthz` endpoint at the configured API base URL; default to `http://127.0.0.1:8080`. Allow configuration without recompilation, for example through `ALYCIA_API_BASE_URL`. Invalid configuration must be shown as an error rather than crashing the client.
3. Report the API **available only** for HTTP `200` with the `Healthy` plain-text body. HTTP `503`, any other HTTP response, an unexpected body, connection refusal or timeout must never be displayed as success.
4. Provide a readable window with initial/checking, available and unavailable/error states; clear feedback and an explicit "Retry" or "Check again" action. Rechecking updates state in both directions without UI blocking or overlapping concurrent requests.
5. Document in English the relevant build and Avalonia launch commands, base URL configuration and manual state verification in the existing `README.md` or a sprint-justified document. Do not conceal API limitations.
6. Produce `docs/Alycia-Sprint-3-SMART.md` (English, after approval) and `docs/Alycia-Sprint-3-END.md` (factual English report, before PR) under the Charter; reuse the PR template already present on `develop`.

## 4. Excluded Scope

- New backend endpoints, redesign of `/healthz`, PostgreSQL health probing or adoption of the business JSON envelope.
- Authentication, sessions, secret vault, business data, EF Core, migrations and the first business module.
- MAUI, Windows testing, MVP parity, software releases or promotion to `testing`/`main`.
- Automatic periodic polling, system notifications, a full dashboard, persisted settings or advanced visual customization.
- Choosing the final UI language; full localization.
- CI/CD, GitHub Actions, artificial UI test suites or unnecessary API refactoring.

## 5. Deliverables

| Deliverable | Expected outcome | Proposed location |
|---|---|---|
| D1 — Avalonia client | `net10.0` project in the solution; desktop app runnable on Linux | `src/Alycia.Client.Avalonia/`, `Alycia.slnx` |
| D2 — HTTP health probe | Asynchronous `/healthz` check, configurable URL, explicit success/failure classification | Inside the client project, minimally separated by actual responsibility |
| D3 — Status UI | Window, checking state, result, error and recheck action | Avalonia client view and presentation state |
| D4 — Startup documentation | Build, Linux launch, base URL and manual verification instructions | `README.md` or focused English documentation |
| D5 — Sprint traceability | English SMART and END in distinct commits; evidence-backed PR | `docs/Alycia-Sprint-3-SMART.md`, `docs/Alycia-Sprint-3-END.md`, GitHub |

## 6. Acceptance Criteria and Expected Evidence

| ID | Mandatory observable outcome | Expected check / evidence |
|---|---|---|
| **AC01** | `Alycia.Client.Avalonia` exists under `src/`, targets .NET 10, is listed in `Alycia.slnx`, and the whole solution builds without errors. | `dotnet sln Alycia.slnx list`; `dotnet build Alycia.slnx` and actual output. |
| **AC02** | The Avalonia window opens on the owner's Linux machine even when the API is unavailable; backend downtime never closes the client. | Run the project on CachyOS and observe behavior with the API stopped. |
| **AC03** | The API base address is configurable without recompilation; default is loopback `http://127.0.0.1:8080`. A malformed address displays an error state without crashing. | Run with default, configured and invalid addresses; capture actual outputs or UI observations. |
| **AC04** | The HTTP health request is asynchronous and time-bounded; "available" is returned **only** for HTTP 200 and text `Healthy`, without requiring PostgreSQL or changing the API. | Focused code inspection, `curl -i` against `/healthz`, and observed UI state. |
| **AC05** | The window visibly shows "checking" and stays responsive during the request; "available" is legible when the API is healthy. | Manually demonstrate initial launch with API running and observe the window. |
| **AC06** | Connection refusal, timeout, non-200 responses or unexpected bodies yield clear unavailable/error state, never a false positive. | Targeted reproduction of feasible cases, UI observation and, if justified, focused classification tests; do not claim unexecuted scenarios. |
| **AC07** | A recheck action works without restarting the window or issuing overlapping concurrent requests, correctly updating state after backend loss and recovery. | Controlled API running → stopped → running cycle, recheck action and owner observations. |
| **AC08** | The actual Avalonia-to-local-API flow works with the existing Docker backend, without modifying `/healthz` or exposing additional host ports. | Relevant `make up` / `make status` and `curl` outputs, Avalonia window; use `make down` only if needed, preserving volumes; actual observations or screenshots. |
| **AC09** | English documentation explains Linux build and launch, API base URL configuration, and available/unavailable checks without claiming unimplemented features. | Inspect README and reproduce any relevant commands not already verified; evidence in END. |

No criterion has passed yet. Code inspection or screenshots do not replace actual execution evidence when the criterion requires it.

## 7. GitHub Issue Plan (owner-created)

**Planned parent issue**

- **Exact title:** `Sprint 3 — Avalonia Backend Status`
- **Description:** `Introduce the first Linux Avalonia desktop client, connect it to the existing technical GET /healthz endpoint, display checking/available/unavailable states, support manual retry, and validate the end-to-end local flow. Track AC01–AC09 with evidence and keep the existing backend contract unchanged.`
- **Labels:** `type:sprint`, `area:client` (propose creating the latter if absent and obtain approval).
- **Actual issue number and URL:** to be filled **after creation**, never invented.

**Coherent deliverable sub-issues**

| Exact title | English description | Planned labels | Covered criteria | Real dependency / parent | Real ID and URL |
|---|---|---|---|---|---|
| `[S3] Bootstrap Avalonia desktop client` | `Create the .NET 10 Avalonia desktop project, add it to Alycia.slnx, and validate its Linux startup independently of API availability.` | `type:task`, `area:client` | AC01, AC02 | S3 parent | Pending |
| `[S3] Implement configurable backend health probe` | `Implement an asynchronous, time-bounded GET /healthz probe with configurable API base URL; classify success, invalid responses, HTTP failures, network errors, and timeouts safely.` | `type:task`, `area:client` | AC03, AC04, AC06 | S3 parent; client established | Pending |
| `[S3] Build status UI and retry workflow` | `Display checking, available, and unavailable/error states; keep the interface responsive and allow retry and recovery without overlapping health checks.` | `type:task`, `area:client` | AC05, AC07 | S3 parent; probe available | Pending |
| `[S3] Verify Linux integration and document usage` | `Demonstrate the Avalonia-to-API health check under CachyOS with backend up/down/up, record observed evidence, and document build, launch, and API configuration.` | `type:task`, `area:client`, `area:docs` | AC08, AC09 | S3 parent; working app | Pending |

**Mapping:** every AC01–AC09 is covered by at least one sub-issue. Link all four to the parent, without creating one issue per criterion. Numbers and URLs must come from actual GitHub creation; never assume labels exist without checking.

## 8. Implementation Plan

1. Verify the state of `develop`, the workstation and tools, and obtain **explicit approval of the French SMART sheet** before coding or committing.
2. After approval, compare the English translation **section by section** against the approved French sheet and correct wording without changing substance. Confirm signing, create `feature/avalonia-backend-status` from `develop`, and make the **first signed commit** containing only `docs/Alycia-Sprint-3-SMART.md`.
3. Create the planned parent and sub-issues (and the client label if approved), link them and record actual numbers/URLs. Issues may be created before the first commit if authorized; never include invented references.
4. Create the Avalonia desktop project and add it to the solution; verify build and window startup on Linux.
5. Add the asynchronous `/healthz` probe, URL configuration, UI states and failure handling; execute only relevant checks after each change.
6. Verify the actual API available → unavailable → available flow without rerunning unrelated Sprint 1 tests. Document client startup in English.
7. Collect the owner's final **read-only** outputs (`git status`, `git branch -avv`, `git log --show-signature`, `dotnet sln ... list`, `docker compose ps`, etc.), remove sensitive values and write `docs/Alycia-Sprint-3-END.md` in English using only real evidence. Commit it separately with signing before opening the PR.
8. With the owner's authorization, open the PR into `develop`; use `.github/pull_request_template.md` from `develop` manually if GitHub does not present it (the template may not yet be on `main`). Link SMART/END, issues, and **one checkbox per AC01–AC09**; merge only after verification.
9. After merge, manually close evidenced sub-issues and then the parent; record merge and closures on GitHub rather than rewriting the pre-PR END.

## 9. Targeted Verification Strategy

### Unit tests

No unit-test project is imposed artificially for a thin status view. If a sufficiently nontrivial independent response classifier is actually extracted, add only focused tests for misleading responses and failures. Do not create test infrastructure that merely duplicates the implementation.

### Integration tests

The essential check is **manual and real** on CachyOS: Avalonia client running, local API healthy then stopped then restarted, verifying state transitions through the recheck action. Use the existing backend; no mandatory fake test endpoint or isolated test environment.

### Manual checks

Verify display, states, window responsiveness, absence of crashes, invalid address handling, expected HTTP response and absence of overlapping requests. Screenshots are optional if actual observed evidence is sufficiently clear.

### Explicitly excluded checks

No MAUI/Windows tests, PostgreSQL migrations, authentication, backups/restores, RAG tests, CI/CD suite or load testing. `/healthz` is not reimplemented.

### Evidence policy

Only the owner executes local commands. The AI reads GitHub only, analyzes supplied real command outputs, claims no evidence without results and does not repeat successful checks absent a change or concrete doubt. No secrets or private data are included in reports or issues.

## 10. Completion Conditions

**Before PR/merge:** AC01–AC09 actually satisfied; French source approved and English translation faithful; all new commits signed (`git commit -S`) and verified on GitHub; English SMART/END under `docs/` with a factual pre-PR END report; PR to `develop` including links, parent issue, sub-issues and evidence-backed checklist. Required Linux checks must have actually run.

**After merge:** the owner closes evidenced sub-issues and then the parent; record merge and closures on GitHub, not prospectively in the pre-PR END report.

**Timing:** record actual SHA and committer dates of first SMART commit and END commit; their interval is **indicative elapsed time**, not active work hours.

**MVP progress:** 0/8 capabilities complete unless a full business capability has been verified end to end and under MAUI where required. No automatic software version.

**Approval state of this sheet: APPROVED on November 5, 2026 by the owner. Sprint execution and all Git/GitHub operations remain the owner's responsibility.**
