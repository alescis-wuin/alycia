# Alycia — Sprint 6: Domain to PostgreSQL Vertical Slice — Pre-PR End Report

- **Sprint:** Sprint 6 — Domain to PostgreSQL Vertical Slice
- **Status:** PRE-PR REPORT — technical evidence reviewed; PR-dependent AC10 remains PENDING.
- **Evidence cutoff:** November 5, 2026, after the signed implementation commit and before any Sprint 6 PR.
- **Repository:** https://github.com/alescis-wuin/alycia
- **Branch / intended PR target:** `feature/domain-postgresql-vertical-slice` → `develop`
- **Approved specification:** `docs/Alycia-Sprint-6-SMART.md` (approved French source and English translation).
- **Published software version:** none. **Completed MVP capabilities:** 0/8; the Workspace/Projects capability is not completed by this slice.

## 1. Delivered scope

- Added `Workspace` within `Workspaces.Domain`, with server-generated nonempty `Guid`, a trimmed required name of at most 100 characters, and a UTC `DateTimeOffset` creation timestamp. No `OwnerUserId`, Projects, Conversations, or other new business entity was introduced.
- Added `CreateWorkspaceService`, `GetWorkspaceService`, and dedicated `IWorkspaceRepository` in Application. The EF Core implementation and `WorkspacesDbContext` reside in Infrastructure; Application does not access SQL or EF Core directly.
- Added the explicit initial Workspaces migration. PostgreSQL stores records in `workspaces.workspaces`, with a `citext` name column and unique index `ux_workspaces_name`; the database constraint handles races between creators.
- Added `make migrate` as the explicit, centralized migration entry point, with module ordering controlled by `scripts/migrate.sh`; no migrations run on API startup. The current script registers Workspaces only.
- Added Workspace create/read API routes and the standard application JSON envelope. Anonymous access requires both `Development` and an explicit opt-in, and is restricted to actual loopback connections. Routes remain absent in Production, even with the opt-in setting. This is the approved temporary S6 exception, not authentication.
- Added an Avalonia create/retrieve workflow with loading, validation, duplicate/conflict, missing-record, disabled/denied, and unavailable states. Preserved the independent technical `/healthz` endpoint.
- Added focused xUnit Domain/Application and Avalonia HTTP-client tests, English setup/security/migration guidance, and the Docker build changes required by the new module projects.

## 2. Git and source evidence

| Evidence | Observed result | Boundary |
|---|---|---|
| S5 integration baseline | Owner previously confirmed `develop` at `cf2a6be9d4f77f76d8289c82ba8261f5c674e269` (PR #32 merged). | Local starting baseline; no claim that later remote branches were updated. |
| First S6 commit | `1441f8d1dedcde4efff545b536fad1a74e2fa693`; only `docs/Alycia-Sprint-6-SMART.md`, signed. | Locally verified during S6; GitHub `Verified` remains to be checked after push. |
| Implementation commit | `64833d4759dc78a9463b1a09dd5912f613feff91`; 31 files, 1,715 insertions and 50 deletions. | Owner's `git log -1 --show-signature` reported a good GPG signature. |
| Worktree after implementation commit | Owner's final `git status --short` produced no entries. | State at that moment, before adding this report. |
| END report commit | **PENDING at report authoring**. | This report must be the only file in its own signed commit; record its resulting SHA in the PR. |
| S6 PR / GitHub signature checks | **PENDING** — no Sprint 6 PR verified at this cutoff. | Check actual target, linked documents/issues, checklist, and `Verified` signatures after opening, before merging. |
| Issue closure | **PENDING**. Parent #33 and sub-issues #34–#37 were opened for S6; their final status must be checked. | Close satisfied issues manually only after merging into `develop`. |

## 3. Acceptance criteria — pre-PR status

| AC | Status | Observed evidence and limits |
|---|---|---|
| **AC01** | **PASS** | Repository diff review found one new business entity (`Workspace`) in the Workspaces module with the approved D1–D3 boundaries. No speculative additional business entity. |
| **AC02** | **PASS** | Focused xUnit tests cover blank/whitespace names, trimming, the 100/101-character boundary, nonempty ID and UTC timestamp; actual PostgreSQL case-insensitive uniqueness and concurrent create conflict were also observed. |
| **AC03** | **PASS** | `make migrate` applied the initial migration and succeeded again without reapplying it; the owner checked the Workspaces migration-history record, `citext`, unique index and database constraint. A non-running-PostgreSQL failure path was verified to return nonzero with an isolated `docker` command stub immediately before creating this report; real container shutdown was not needed. API startup does not apply migrations. Persistence after API restart was observed. |
| **AC04** | **PASS** | Source/dependency review confirms dedicated Application repository and EF Infrastructure implementation. Service tests passed; actual HTTP create/read/invalid/not-found/conflict results were observed, including exactly one winner under concurrent creation. |
| **AC05** | **PASS** | Local HTTP smoke checks confirmed absent routes without Development opt-in and in Production even with opt-in, actual enabled loopback create/read with correct statuses/envelope, and blocked Docker-container access to the host-only listener. `/healthz` remained available without database access. No anonymous LAN/VPN exposure was enabled for testing. |
| **AC06** | **PASS** | The owner completed the actual CachyOS Avalonia walkthrough and reported all steps successful: create, retrieve, duplicate, validation, missing UUID, API restart, opt-in removal and backend shutdown. This is an operator observation, not an archived screenshot or automated UI test. |
| **AC07** | **PASS** | Created records remained retrievable after restarting the API; the Avalonia restart/read path was confirmed. The direct PostgreSQL concurrency check found exactly one record for the competing name. Invalid names are rejected before persistence; the evidence does not claim a full-table audit of all historical test data. |
| **AC08** | **PASS** | For actual HTTP `201`, `409` and induced `503` responses, the owner verified matching `traceId` values and request durations in structured JSON Lines logs. The targeted log check found no connection-string password field; this is not an exhaustive secret-leak audit. |
| **AC09** | **PASS** | Final local `make check`: lint passed; 34 xUnit tests passed, 0 failed, 0 skipped. API Debug build and Docker Release image build passed. English README and Workspaces guide cover explicit migrations, future ordering/partial-failure limits, local D2 security and operation. No CI/CD was added. |
| **AC10** | **PENDING** | French/English SMART approved; SMART-only and implementation commits locally signed. This factual English END is prepared for a separate signed pre-PR commit. PR target, actual links, AC01–AC10 checklist, evidence and GitHub `Verified` signatures can only be validated after PR creation. |

**Pre-PR tally: 9 PASS / 1 PENDING / 0 FAIL.** This is not sprint closure.

## 4. Actual checks and results supplied by the owner

| Check | Observed outcome | AC |
|---|---|---|
| `make migrate` twice against running PostgreSQL | First application succeeded; repeat was idempotent. Migration history recorded the initial Workspaces migration once. | AC03 |
| PostgreSQL constraint inspection and rolled-back case-variant insert | `ux_workspaces_name` raised the expected uniqueness violation; transaction was rolled back without leaving the probe record. | AC02–AC04 |
| Workspace API smoke (local Development, opt-in enabled and disabled; Production) | Disabled/Production routes absent; enabled HTTP `400/404/201/200/409` cases succeeded; Docker-container access did not reach the loopback listener; persisted record remained readable after API restart. | AC04–AC05, AC07 |
| Avalonia interactive operator walkthrough | All prompted steps reported successful, including states and persistence after restart, disabled route and unavailable backend. | AC06–AC07 |
| Concurrent HTTP creation + PostgreSQL count | Exactly `201` and `409`, one persisted record with trimmed name. | AC02, AC04, AC07 |
| Deliberately unreachable PostgreSQL + `/healthz` | `503 DEPENDENCY_UNAVAILABLE` while `/healthz` remained `Healthy`. Rechecked successfully after the final API exception-classification fix. | AC04–AC05, AC08 |
| Structured response/log correlation | HTTP `201`, `409`, `503` each had matching trace IDs and request durations; targeted password-field scan passed. | AC08 |
| Final `make check` after review fixes | Lint passed; 34/34 xUnit tests passed, 0 failed, 0 skipped. | AC02, AC04, AC06, AC09 |
| API Debug compilation and Docker API image build after review fixes | Both succeeded, with no reported build warnings. | AC05, AC09 |
| `git diff --cached --check`, staged-file review and signed implementation commit | 31 scoped files; `64833d4` locally reported a good GPG signature; clean short status after commit. | AC01, AC09–AC10 |
| Non-running PostgreSQL migration failure simulation (END preparation) | `scripts/migrate.sh` exited nonzero when an isolated CLI stub reported no running PostgreSQL container; actual Docker state was left unchanged. | AC03 |

### Investigation and correction history

- The original host-side HTTP smoke used an Npgsql connection timeout of 3 seconds. A separate diagnostic connected successfully and ran `SELECT 1` with a 15-second allowance, taking approximately **7.8 seconds**. The temporary HTTP verification timeout was increased; **the underlying cause of this latency was not identified**.
- The API was corrected to report `503 DEPENDENCY_UNAVAILABLE` for actual nested database-connectivity failures rather than a generic `500`; a subsequent review narrowed the classifier to avoid labeling unrelated update errors as dependency outages.
- EF Core package-version alignment and generated-file formatting issues were corrected during implementation. Two obsolete Avalonia `Watermark` properties were replaced with `PlaceholderText`. The README was updated to reflect the completed S6 Avalonia UI.
- Relevant checks were rerun after these corrections; the final reported test count is **34 passing tests**.

## 5. Data, security and remaining limits

- Nonsensitive local verification records were deliberately retained in PostgreSQL, including `02f3deb8-3ea7-4a83-b788-ba5f4462e210` (`S6Smoke-8c661d89`) and `e1eb6e37-332b-419f-b81c-b5720bbabc65` (`S6Race-1072f15abb25`). The manual UI run may have created another test Workspace; its UUID was not recorded in the supplied output. No cleanup of these records is claimed.
- The **S6-only** anonymous local Development opt-in is not an authorization system and must not be carried forward as a general anonymous API policy. Owner identity, Workspace ownership, login, tokens, revocation and production business-route protection are outside S6.
- The `OwnerUserId` migration and transactional owner attachment are deferred to the approved design for later Identity implementation; S6 did not implement them.
- MAUI/Windows parity, Projects, Conversations, other module migrations, load/performance benchmarks, real production deployment and CI/CD are outside this sprint. No MVP capability is marked complete.
- The Docker-bridge Npgsql latency remains unexplained. The observed successes apply to the tested local CachyOS/Docker configuration, not to all networks or production reliability.
- The targeted tests covered JSON Lines correlation and absence of one sensitive connection-string field; a universal proof that no sensitive data could ever be logged is not claimed.
- No active-hours timesheet was supplied. Git commit timestamps describe calendar events, **not measured active development time**.

## 6. Handoff and required post-report checks

1. Add **only** this END file in its own signed commit before opening the Sprint 6 PR. Verify the signature locally; record the resulting END commit SHA in the PR (not retroactively in this report).
2. Push the signed S6 branch only when the owner elects to do so; open a PR **toward `develop`**, linking the actual parent issue #33, sub-issues #34–#37, `docs/Alycia-Sprint-6-SMART.md`, and this END file.
3. In the open PR, verify an explicit checkbox and supporting evidence for **each of AC01–AC10**, correct branch target, issue/document links, and GitHub **`Verified`** status of the signed commits. Only then change AC10 to PASS **in the PR**; this immutable pre-PR report remains PENDING.
4. Merge only after all mandatory ACs are actually satisfied. After merging into `develop`, manually close satisfied sub-issues #34–#37 and then parent #33. Do not promote `testing`/`main` or publish a software version automatically.

**Pre-PR conclusion:** the Workspace vertical slice has sufficient reported local evidence for AC01–AC09; AC10 awaits the actual PR and remote signature/link review. Sprint 6 remains open.
