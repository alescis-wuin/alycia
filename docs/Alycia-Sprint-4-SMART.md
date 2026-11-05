# Alycia — Sprint 4: SMART Specification

**Sprint:** Sprint 4 — Development Tooling and Test Foundation
**Status:** **APPROVED — not started**
**Type:** cross-cutting technical foundation; no new MVP capability
**Authoritative source:** the French SMART sheet, explicitly approved on November 5, 2026; this English translation was reviewed after approval.
**Official repository:** https://github.com/alescis-wuin/alycia — AI access through the GitHub connector is read-only.
**Proposed branch:** `chore/development-tooling-test-foundation`, created from `develop` **after Sprint 3 has closed**
**PR target:** `develop`; no software release.
**Known MVP progress:** 0/8 completed capabilities; reassess at sprint start.
**References:** Alycia Charter revision 1.11; Consolidated Scoping revision 14; `C08.P12` (local observability), `C08.P13` (local tests), `C08.P18` (deferred validation tools), `C04.P07` (deferred secondary libraries).

> **Schedule APPROVED on November 5, 2026 (planning sequence):** Sprint 4 covers tooling and tests instead of the originally planned first persistence slice. The schedule places “API Contracts and Developer Diagnostics” in Sprint 5, the first persistence slice in Sprint 6, and identity and sessions work in Sprints 7–9. **Approving this schedule does not approve the SMART specifications or still-deferred technical choices for Sprints 5–9.** The charter and scoping document have been aligned with this decision without rewriting history. Sprint 3 has **merged into `develop`** (PR #20) and its issues are closed.

## 1. SMART Objective

**Objective:** make local Alycia development more reliable and less repetitive by adding lightweight formatting rules, Makefile quality commands, local Git whitespace checks with opt-in hook installation through `make install-hooks`, at least one unit-test project targeting real behavior, and an end-to-end verification of the existing console/file logging.

- **Specific:** standardize text files, correct unnecessary whitespace before `git add`, verify the Git index without rewriting it before commit, run tests through a simple command, and confirm useful Serilog events.
- **Measurable:** working Makefile targets (including `make install-hooks`) with reliable exit codes, demonstrated detection of deliberately introduced whitespace in a temporary working file, tests executed with actual results, and examples of valid console messages and JSON events.
- **Achievable:** .NET 10, Git, Make, Serilog and the API already exist; unit tests will exercise actual Sprint 3 code without inventing an artificial layer.
- **Relevant:** reduce manual fixes and simplify diagnostics before persistence and business APIs are added.
- **Time-bounded:** **3 to 5 indicative sessions of 45–90 minutes** (about 2 hours 15 minutes to 7 hours 30 minutes of estimated active effort). Continue the same sprint if any AC is unmet. The interval between SMART and END commits is calendar time, not a measure of active work.

**Expected final demonstration:** on CachyOS, the owner formats changed files before staging, receives an explicit failure if the index contains forbidden whitespace, runs local tests with one command, and finds the expected timestamped, correlated `/healthz` events without exposing sensitive data.

## 2. Initial State, Prerequisites and Decisions

- **Latest remote read:** `develop` at `29ca210ba3133e94183f2182e6c8ea4351941ca7`, containing `Alycia.Api`, `Alycia.Client.Avalonia`, Serilog and a Makefile (`build`, `up`, `status`, `down`). No `.editorconfig`, `.gitattributes`, versioned Git hook or test project is visible there. **This snapshot may become stale:** recheck `develop` at Sprint 4 startup.
- **Sprint 3:** **COMPLETED according to the remote repository**: [PR #20](https://github.com/alescis-wuin/alycia/pull/20) merged, issues #15–#19 closed; review its code and END before selecting actual Sprint 4 tests. Do not treat remote verification as local execution.
- **Local prerequisites:** a clean worktree or safely preserved changes; existing .NET 10 SDK, Make, Git and Docker environment; confirm installed tools and working GPG/SSH commit signing before the first commit.
- **Existing behavior to retain:** Serilog + `ILogger`, console level/timestamp, persistent JSON Lines, rolling/retention, `TraceId` and HTTP duration; `GET /healthz` remains technical, outside the business envelope and without a PostgreSQL check.
- **APPROVED test framework:** **xUnit**, through standard `dotnet test`, approved by the owner on November 5, 2026. This separate decision preceded approval of the complete Sprint 4 SMART specification.
- **Decisions required up front:** the revised schedule and xUnit are approved; **the complete French SMART specification was explicitly approved on November 5, 2026**. Review Sprint 3 code to identify meaningful test behavior. No additional dependency is imposed in advance.

## 3. Included Scope

1. Add a concise `.editorconfig` and, if useful, `.gitattributes` for tracked text files: UTF-8, consistent line endings, C# and Makefile-appropriate indentation, final newline, and removal of trailing whitespace. Avoid unnecessary repository-wide normalization or reformatting.
2. Add a simple **pre-staging cleanup** path: `make format` uses `dotnet format` for C# and removes unnecessary trailing whitespace from **relevant modified text files**, without touching binaries, generated files, secrets or the Git index. Preserve content and inspect the diff before `git add`.
3. Add **non-mutating** `make lint` checks to detect formatting discrepancies and Git whitespace errors. Provide a versioned, **opt-in `pre-commit` hook activated only by `make install-hooks`**, which checks at least `git diff --cached --check`, rejects errors and performs neither automatic `git add` nor commits. `make install-hooks` configures this Git repository only (for example via local `core.hooksPath`), is idempotent and does not replace existing hook configuration or hooks; if there is a conflict, it stops with an explanation. Neither `make build`, `make check` nor cloning may install hooks implicitly.
4. Extend the Makefile with documented `format`, `lint`, `test`, `check` and `install-hooks` commands, keeping the semantics of `build`, `up`, `status` and `down`. `check` combines only relevant checks without gratuitous duplicate builds or CI/CD.
5. Add **at least one .NET 10 unit-test project** under `tests/`, included in `Alycia.slnx`, with **genuine tests** of testable post-Sprint-3 behavior (prefer health-response classification, network failures or health-status transitions). Use in-memory HTTP simulation when useful; these tests must not require starting the Avalonia UI or Docker. Introduce only a small testability seam if actually necessary.
6. Verify existing API console and JSON Lines events, prefixes/levels, timestamps, `TraceId`, status codes and elapsed durations; assess colors in a capable terminal. Fix only an objectively observed defect. Keep the existing rotation and volumes without introducing complex instrumentation.
7. Document in English the local workflow, optional hook activation via `make install-hooks`, cleanup/check/test commands, `format` → diff review → `git add` → signed commit sequence and log inspection. Prepare English `SMART` and `END` documents according to the charter.

## 4. Excluded Scope

- A replacement logging engine, centralized collection, telemetry pipeline, distributed OpenTelemetry, monitoring dashboard, continuous profiling or benchmarks.
- Full request/response recording in a database, a custom Web explorer, filters, JSON exports or sensitive-content instrumentation.
- OpenAPI, Scalar and new application contracts: outside Sprint 4, scheduled for Sprint 5 under the approved plan; its SMART specification still requires approval.
- CI/CD, GitHub Actions, remote automation, an additional Docker service or global Git-hook installation.
- Broad refactoring of Avalonia, the API foundation or business modules; artificial tests or heavy GUI test suites.
- Changes to `/healthz`, its lack of PostgreSQL checks, network security or the API response envelope.
- Empty test projects for future modules, PostgreSQL schema changes, migrations, authentication or software releases.

## 5. Deliverables

| Deliverable | Expected outcome | Proposed location |
|---|---|---|
| D1 — Text conventions | Simple rules respecting file types and line endings | `.editorconfig`, `.gitattributes` if useful |
| D2 — Formatting and checks | `make format`, `make lint`, `make install-hooks`, hook checking the index without rewriting it | `Makefile`, `.githooks/pre-commit` and minimal supporting documentation if needed |
| D3 — Useful tests | .NET 10 project and focused unit cases, run with `make test` | `tests/<actual-project>.Tests/`, `Alycia.slnx` |
| D4 — Serilog verification | Actual console/file event evidence, focused fix only if defective | `Alycia.Api` only if essential; END report |
| D5 — Developer guide | Commands, optional hooks, troubleshooting and development sequence | `README.md` or necessary English technical documentation |
| D6 — Sprint traceability | SMART translated after approval, English END and PR | `docs/Alycia-Sprint-4-SMART.md`, `docs/Alycia-Sprint-4-END.md` |

## 6. Acceptance Criteria and Evidence

| ID | Required observable outcome | Expected verification |
|---|---|---|
| **AC01** | `.editorconfig` and, if selected, `.gitattributes` rules apply to relevant text files without unjustified mass rewrites or alteration of generated/binary files. | Inspect rules, `git diff --stat` and targeted diff; verify extensions and line endings. |
| **AC02** | `make format` fixes trailing whitespace in a working text file and formats relevant C# **before `git add`**, without changing the index or unrelated files; a second run makes no further changes. | Trial with an isolated/temporary file, `git diff` and `git diff --cached` before/after; idempotence check. |
| **AC03** | `make lint` changes no files, passes on compliant code and exits non-zero with a useful diagnostic when formatting or whitespace errors are present. | Run against a clean case and an isolated defect; verify repository state is unchanged by the check. |
| **AC04** | `make install-hooks` installs the versioned `pre-commit` hook **only when explicitly requested** for this repository without overwriting existing configuration; the command is idempotent and clearly reports any conflict. The hook rejects staged whitespace errors, changes neither the index nor files, and provides an actionable fix. | Install twice in a temporary test repository; verify local Git configuration, handling of a conflicting pre-existing setting, and rejection of staged whitespace via `git diff --cached --check`, with no effect on global configuration. |
| **AC05** | At least one .NET 10 test project is included in `Alycia.slnx` and genuinely verifies multiple outcomes of the selected post-Sprint-3 behavior: success, unexpected response, network error and timeout, **when those cases belong to that responsibility**. No dummy tests or UI startup required. | `dotnet sln Alycia.slnx list`, inspect test cases and `dotnet test` with more than zero tests reported. |
| **AC06** | The Makefile retains its four existing targets and provides `format`, `lint`, `test`, `check`, `install-hooks`; test and check commands return meaningful exit codes, without CI/CD or redundant validation. | Targeted `make -n`/inspection, `make test` and then `make check` as needed without rerunning an already-successful check. |
| **AC07** | A local `/healthz` request generates the expected diagnostic events: level, timestamp, `TraceId` identity/correlation, HTTP status and elapsed duration; JSON Lines are readable and console output has an intelligible prefix. | `curl`, focused `docker compose logs` and actual JSON Lines events; observed fields recorded. |
| **AC08** | Console diagnostics remain readable and colored **where the terminal and logging engine support it**, and do not record request bodies, authorization headers or secrets; existing persistence/rolling settings are preserved. | Interactive observation where supported, focused code/configuration review and redacted event examples; disclose limitations accurately. |
| **AC09** | English documentation permits reproduction of `format`, `lint`, `test`, `check`, `install-hooks`, with explicit opt-in hook activation, cleanup before staging and log inspection without promising CI or deferred tools. | Inspect and execute relevant commands not yet checked; confirm README/Makefile/hook consistency. |
| **AC10** | The French SMART is approved and faithfully translated before the first **signed commit containing only `docs/Alycia-Sprint-4-SMART.md`**; the factual English END is added in a separate signed commit **before opening the PR**; **after opening and before merging**, the PR into `develop` is checked for actual SMART, END and issue links and one checkbox per AC01–AC10. **AC10 passes in full only after that PR check.** | **Before the PR:** Git history, local signatures and documents; **after opening and before merging:** PR target and content, links, checklist, actual evidence and GitHub “Verified” signatures. Issues are closed only after merge. |

**These ACs are goals, not achievements.** The owner performs local checks; the AI does not claim to have executed them.

## 7. GitHub Issue Plan (Owner-Created)

**Parent issue**

- **Exact title:** `Sprint 4 — Development Tooling and Test Foundation`
- **Description (English):** ``Consolidate local developer tooling for Alycia after Sprint 3: define formatting and whitespace rules, add non-destructive local quality checks and a Git hook installed only via `make install-hooks`, introduce meaningful unit tests, verify existing console and structured file logs, and document the workflow. Track AC01–AC10 with actual evidence; do not introduce CI/CD or advanced observability.``
- **Proposed labels:** `type:sprint`, `area:infrastructure`.
- **SMART link:** `docs/Alycia-Sprint-4-SMART.md` (real URL only after push).
- **Real number and URL:** fill in after creation.

| Exact sub-issue title | English description | Proposed labels | Covered AC | Actual dependencies | Number/URL |
|---|---|---|---|---|---|
| `[S4] Establish formatting rules and safe Git hooks` | ``Add minimal text formatting rules, a pre-staging whitespace cleanup command, read-only lint checks, an explicit idempotent `make install-hooks` target and an opt-in pre-commit hook that rejects staged whitespace errors without rewriting the index or overriding existing hooks.`` | `type:task`, `area:infrastructure` | AC01–AC04 | Parent issue; Sprint 3 completed | Pending |
| `[S4] Add Makefile quality targets and focused unit tests` | ``Preserve existing Makefile commands; add format/lint/test/check/install-hooks and a .NET 10 unit-test project covering real post-Sprint-3 behavior without UI or Docker dependencies.`` | `type:task`, `area:infrastructure` | AC05, AC06 | Parent issue; S3 code integrated | Pending |
| `[S4] Verify console and structured file logging` | ``Confirm readable timestamped console events, structured JSON Lines, trace correlation, HTTP status and timing, privacy and existing retention configuration; fix only observed gaps.`` | `type:task`, `area:observability` | AC07, AC08 | Parent issue | Pending |
| `[S4] Document developer workflow and sprint evidence` | ``Document local formatting, testing, hook installation via `make install-hooks` and log inspection; prepare the factual English END report and an evidence-backed signed-commit PR checklist.`` | `type:task`, `area:docs` | AC09, AC10 | Parent issue; other deliverables ready for END | Pending |

Link all four sub-issues to their parent. Check actual labels before use. Never assume GitHub issue numbers.

## 8. Implementation Steps and Indicative Effort

| Indicative session | Steps | Intermediate outcome |
|---|---|---|
| **1 — 45–90 min** | Confirm S3 closure, inspect its code and `develop`; confirm approval of the French SMART and review its English translation. Prepare branch and signing. Make the first `git commit -S` with **only the English SMART**. Create parent/sub-issues if authorized without inventing `Refs`. | Scope stabilized, English specification versioned and real issue plan. |
| **2 — 45–90 min** | Set up `.editorconfig`, whitespace cleanup, `format`/`lint`/`install-hooks` commands; verify optional, idempotent hook installation without overwriting existing hooks, and check compliant/defective cases without implicitly modifying the index. | Reproducible formatting and detected whitespace errors. |
| **3 — 45–90 min** | Create the test project and useful cases from S3 code; add `test`/`check` to the Makefile; run targeted verification. | Actual local tests and working commands. |
| **4 — 45–90 min** | Observe a `/healthz` request and console/JSON Lines logs; fix only observed defects; finish documentation. | Verified diagnostics and documented workflow. |
| **5 — if necessary** | Collect read-only evidence, write the END in English, make a separate signed commit, verify signatures, prepare PR/AC checklist and close issues only after merge. | Complete evidence-based sprint handoff. |

The schedule may adapt to actual difficulties. **Do not begin Sprint 4 before its SMART sheet is approved**, even though Sprint 3 has now merged, or weaken ACs to satisfy the estimate.

## 9. Targeted Verification

**Unit tests:** after S3, choose a testable responsibility without a UI, such as HTTP 200 + `Healthy` classification, 503, unexpected text, network exceptions and timeout. Use local HTTP simulation without creating a replacement backend. Do not add tests that merely mirror the implementation.

**Essential integration tests:** targeted verification of an existing `/healthz` API request and its console/file log trace. Do not rerun old PostgreSQL or volume-persistence tests without a concrete reason.

**Manual checks:** compare diffs before/after formatting, opt into the hook with `make install-hooks` (including repeat installation without overwriting existing hooks), check exit codes, observe console output (colors only where ANSI/TTY is supported), review redacted logs.

**Explicitly excluded:** Windows/MAUI tests, new automated UI suites, benchmarks, load tests, OpenTelemetry measurements, request exports and unrelated business tests. No CI/CD.

**Execution policy:** stop when relevant checks pass. Diagnose failures without weakening tests and rerun only affected checks. Never expose secrets in reports, PRs or logs.

## 10. Completion Conditions

**Before opening the PR:** AC01–AC09 demonstrated; the pre-PR documentary portion of AC10 verified (approved French SMART, faithful translation, first signed SMART-only commit, factual English END in a separate signed commit). Relevant tests, documentation and applicable checks must be supported by evidence. **The pre-PR END report marks the PR verification as PENDING and does not claim AC10 has passed in full.**

**After opening the PR, before merge:** verify the `develop` target, actual SMART, END and issue links, one checkbox and supporting evidence per AC01–AC10, and GitHub “Verified” signatures. **AC10 passes only after this verification; do not merge while any mandatory AC lacks evidence.**

**After merge:** the owner manually closes satisfied sub-issues, then the parent; merge and closures are not claimed in the pre-PR END report.

**Dependency and decisions:** Sprint 3 is complete according to GitHub; the S4–S9 planning schedule and xUnit were **APPROVED by the owner on November 5, 2026**. Future SMART specifications and their technical choices still require individual approval. Charter revision 1.11 and Scoping revision 14 record the approved schedule without rewriting historical decisions. **The complete Sprint 4 SMART was explicitly APPROVED on November 5, 2026.**

**MVP progress:** no additional capability can be declared complete solely because of this technical sprint. **Specification status: APPROVED — not started as of approval.**
