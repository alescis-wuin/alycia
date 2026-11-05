# Alycia — Sprint 3: Avalonia Backend Status — Pre-PR End Report

- **Report type:** factual pre-PR snapshot; not a record of a merged pull request or administrative sprint closure.
- **Sprint:** Sprint 3 — Avalonia Backend Status
- **Approved SMART:** `docs/Alycia-Sprint-3-SMART.md` (French authoritative source explicitly approved by the owner on November 5, 2026).
- **Repository:** https://github.com/alescis-wuin/alycia
- **Working branch:** `feature/avalonia-backend-status`
- **PR target:** `develop`
- **Evidence date:** November 5, 2026; based on owner-provided terminal outputs and UI observations through the final local build and AC02 recheck.
- **Software release:** None published or requested.
- **Completed MVP capabilities:** 0/8 (Sprint 3 is a technical foundation).

## 1. Delivered Scope

- Added `src/Alycia.Client.Avalonia/Alycia.Client.Avalonia.csproj` targeting `net10.0` and registered the project in `Alycia.slnx`. The generated client uses Avalonia 12.1.4.
- Added `BackendHealthProbe.cs`: asynchronous HTTP GET `/healthz`, configurable via `ALYCIA_API_BASE_URL`, default `http://127.0.0.1:8080`, five-second timeout, strict success classification (`200 OK` and exact body `Healthy`), explicit error results, and non-local HTTP rejection.
- Added an Avalonia window showing checking, available, and unavailable states, explanatory messages, and a manual `Check again` action. The action is disabled while a request is active; an `_isChecking` guard prevents overlapping checks.
- Updated `README.md` with Linux build and launch instructions, API base URL configuration, manual available/unavailable/recovery checks, and explicit limitations.
- Kept the existing ASP.NET Core `/healthz` endpoint unchanged. No business endpoint, EF Core migration, authentication, MAUI client, Windows check, or CI/CD was introduced.

The owner supplied the staged file inventory before committing the implementation: `Alycia.slnx`, `README.md`, and eight files under `src/Alycia.Client.Avalonia/` (10 changed files total). The implementation commit records 447 insertions and 10 deletions.

## 2. Environment, Branch, and Git Evidence

| Item | Observed result | Evidence source |
|---|---|---|
| Development environment | CachyOS/Linux with an operational graphical desktop | Owner's actual Avalonia launches and screenshots; no distribution/version command was supplied |
| .NET SDK | `10.0.403` | `dotnet --version` |
| Avalonia packages | `Avalonia`, `Avalonia.Desktop`, `Avalonia.Themes.Fluent`, `Avalonia.Fonts.Inter` version `12.1.4`; diagnostic support `2.2.3` | Owner-provided `.csproj` contents |
| Solution | `Alycia.Api` and `Alycia.Client.Avalonia` | `dotnet sln Alycia.slnx list` |
| Starting integration branch | `develop` / `origin/develop` both `eeed2a82330d1a8031b44d630bb276bda7190ecb` at branch creation | Owner's initial `git rev-parse` and `git branch -avv` |
| Working branch / cleanliness | `feature/avalonia-backend-status`; clean worktree before this report was prepared | `git status --short --branch`, `git status`, `git branch -avv` |
| Remote | `origin` = `git@github.com:alescis-wuin/alycia.git` for fetch and push | Earlier owner-provided `git remote -v` |
| SMART-only commit | `a3e87c0cacc71de6679311ff3b62f0b076e8353a`; November 5, 2026, 06:18:15 CET | `git log -1 --show-signature --format=fuller`; file inventory showed only `docs/Alycia-Sprint-3-SMART.md` |
| Implementation commit | `b79267d96edf4364cd58ccb494a57d2387c2aeb1`; November 5, 2026, 11:15:49 CET | `git log -1 --show-signature --format=fuller`; `git show --format= --stat HEAD` |
| Local commit signatures | Both commits verified locally as good GPG signatures using the configured OpenPGP key | Owner's `git log --show-signature` outputs |
| Remote commit signatures | **PENDING** — GitHub `Verified` status not yet checked because the sprint branch had not been published in the supplied evidence | No remote verification supplied |
| GitHub issues | Parent [#15](https://github.com/alescis-wuin/alycia/issues/15); child issues [#16](https://github.com/alescis-wuin/alycia/issues/16), [#17](https://github.com/alescis-wuin/alycia/issues/17), [#18](https://github.com/alescis-wuin/alycia/issues/18), [#19](https://github.com/alescis-wuin/alycia/issues/19); all open and linked as native sub-issues to #15 | Owner's `gh` output and read-only GitHub connector verification during the sprint |
| END report commit | **PENDING** — this file must be committed separately with `git commit -S`; its own SHA and date must be recorded in the later PR | Not yet performed |

## 3. Acceptance Criteria

| Criterion | Status | Actual evidence / qualification |
|---|---|---|
| **AC01** — Avalonia .NET 10 project in solution; full solution builds | **PASS** | `dotnet sln Alycia.slnx list` showed both projects. `dotnet build Alycia.slnx` succeeded, and final `dotnet build` after `dotnet clean`/`dotnet restore` built both projects successfully in 2.8 seconds. |
| **AC02** — Linux client opens independently of API availability | **PASS** | Owner stopped the API with `docker compose stop api`, launched the client using `dotnet run --no-build --project src/Alycia.Client.Avalonia/Alycia.Client.Avalonia.csproj`, and confirmed window opened, displayed `Unavailable`, and closed normally; screenshot provided. |
| **AC03** — Base URL configurable; invalid address handled without crash | **PASS** | Client defaults to `127.0.0.1:8080`. `ALYCIA_API_BASE_URL=http://127.0.0.1:18080` was used for a local HTTP test server without recompilation. `ALYCIA_API_BASE_URL=invalid-url` showed `Unavailable` and `Invalid API base URL.`; screenshot provided. |
| **AC04** — Asynchronous, bounded health check; strict HTTP 200 + `Healthy` | **PASS** | Focused review of `BackendHealthProbe.cs` confirmed asynchronous `HttpClient.GetAsync`, cancellation after five seconds, and exact-body comparison. `curl -i http://127.0.0.1:8080/healthz` returned `HTTP/1.1 200 OK`, `Content-Type: text/plain` and body `Healthy`; the real client displayed `Available`. No PostgreSQL probe was added to `/healthz`. |
| **AC05** — Visible checking / available states, responsive UI | **PASS** | Screenshots showed `Checking...` with the retry button disabled, and `Available` with `Backend available.`. The owner observed a working window during launches and checks. |
| **AC06** — Errors and unexpected results never produce false availability | **PASS** | Owner-provided screenshots showed `Unavailable` for API connection refusal, malformed URL, HTTP 503, HTTP 200 with unexpected body, and a simulated request delayed beyond five seconds (timeout). The test server was bound to local `127.0.0.1:18080`; these cases were tested without changing the existing API. |
| **AC07** — Manual retry, recovery, and no overlapping checks | **PASS** | Owner confirmed `Available → Unavailable → Available` after stopping and restarting the API and pressing `Check again` without restarting the client. Checking-state screenshot and code review demonstrated a disabled button plus `_isChecking` guard. No separate concurrency stress test was claimed. |
| **AC08** — End-to-end Avalonia-to-local-Docker-API flow | **PASS** | Actual local Docker backend, `curl` health response, and Avalonia UI were observed. Docker API logs after `docker compose up` showed startup and two `GET /healthz` requests returning HTTP 200. The owner separately confirmed both API outage and recovery behavior. No extra published port or backend health endpoint change was introduced in the staged implementation. |
| **AC09** — English documentation of Linux launch and verification | **PASS** | Owner supplied the complete updated `README.md` content. It describes .NET 10 build, Linux client launch, URL override, manual availability/recovery/invalid URL procedures, and health-check limitations. `git diff --check` and full solution build succeeded after the documentation update. |

**Acceptance evidence tally:** **9 PASS, 0 FAIL, 0 PENDING** for the nine technical acceptance criteria, based exclusively on the evidence above. Administrative pre-PR requirements remain separate and are listed below.

## 4. Executed Commands and Observations

The following commands and results were supplied by the owner; this report does not claim independent execution on the owner's workstation.

| Executed check | Reported result | Relevant criteria |
|---|---|---|
| `dotnet --version` | `10.0.403` | Prerequisites |
| `dotnet new list avalonia` | Avalonia desktop templates available | Prerequisites |
| `dotnet sln Alycia.slnx list` | `src/Alycia.Api/Alycia.Api.csproj` and `src/Alycia.Client.Avalonia/Alycia.Client.Avalonia.csproj` | AC01 |
| `dotnet build Alycia.slnx` | Both projects succeeded; reported duration 0.9 seconds on the documentation-review run | AC01, AC09 |
| `dotnet clean`, `dotnet restore`, `dotnet build` | All succeeded; last full build completed in 2.8 seconds | AC01 |
| `git diff --cached --check` | No output after line-ending normalization | Source hygiene |
| `git status --short --branch` | `## feature/avalonia-backend-status` (clean) | Git evidence |
| `git log -2 --show-signature --format=fuller` | Local GPG good signatures for the SMART and implementation commits | Git evidence |
| `curl -i http://127.0.0.1:8080/healthz` | HTTP 200, plain text `Healthy` | AC04, AC08 |
| `docker compose stop api`; `docker compose up` | API stopped, then started with PostgreSQL container healthy; API logged HTTP 200 health requests | AC02, AC07, AC08 |
| `dotnet run --no-build --project src/Alycia.Client.Avalonia/Alycia.Client.Avalonia.csproj` | Window opened and closed correctly, showing the expected available or unavailable state in the respective scenarios | AC02, AC05, AC07, AC08 |
| `env ALYCIA_API_BASE_URL=invalid-url dotnet run --no-build --project src/Alycia.Client.Avalonia/Alycia.Client.Avalonia.csproj` | `Unavailable`; `Invalid API base URL.`; screenshot | AC03, AC06 |
| Local temporary HTTP server on `127.0.0.1:18080`, followed by configured Avalonia launch | Screenshots confirmed HTTP 503, unexpected response body, checking state and timeout | AC03, AC05, AC06 |

`docker compose ps` output was not provided; service state is supported instead by the actual Compose startup log and the HTTP/UI observations. The unsuccessful `systemctl stop api` command was an attempt to stop a nonexistent systemd unit, not a failed health-check test; the API was correctly managed through Docker Compose.

## 5. Risks, Limitations, and Exclusions

- The local backend availability result checks only the ASP.NET Core health endpoint; it does **not** establish PostgreSQL readiness, authenticated access, AI provider connectivity or business functionality.
- Non-local HTTPS has not been tested. Only the local loopback workflow was part of Sprint 3 validation.
- MAUI, Windows execution and client parity are deferred. No EF Core model/migration, business endpoint, authentication, backup/restore test, RAG feature or CI/CD was included.
- No artificial unit-test suite was added for this thin status view. The relevant HTTP classifications were exercised through the temporary local test server and observed in the UI.
- No evidence of GitHub's remote `Verified` status for the two commits has been supplied yet; verify after the owner pushes the sprint branch.
- This report is prepared **before** its own commit and before any Sprint 3 PR, merge or issue closure. It must not be amended to predict those events.
- The first SMART and implementation commit timestamps span **4 hours, 57 minutes and 34 seconds** of calendar time; this is not a measure of active work. The complete SMART-to-END interval cannot be calculated until the END commit exists.

## 6. Pre-PR Handoff

- [x] Owner-provided evidence for AC01–AC09 reviewed and recorded.
- [x] Clean working branch and local signatures of the first two sprint commits verified from owner outputs.
- [x] English documentation and existing technical contract preserved; no secrets copied into this report.
- [ ] Owner adds **only** `docs/Alycia-Sprint-3-END.md` in a separate signed commit (`git commit -S`), then supplies the resulting SHA and signature output.
- [ ] Owner publishes `feature/avalonia-backend-status` and verifies GitHub `Verified` signatures for the sprint commits.
- [ ] Owner creates the PR into `develop` with direct links to the versioned SMART and END files, issues #15–#19, and **one checkbox per AC01–AC09** supported by the evidence above.
- [ ] Owner reviews and merges the PR only after required verification. Satisfied sub-issues and then parent #15 are closed manually **after** the merge.

**Pre-PR status:** **Technical acceptance evidence complete; signed END commit, remote signature checks, PR review and merge still pending.** No software release and no new completed MVP capability (0/8).
