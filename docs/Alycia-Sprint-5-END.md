# Alycia — Sprint 5: API Contracts and Developer Diagnostics — Pre-PR End Report

> **Pre-PR evidence snapshot.** This report records only checks and observations supplied by the repository owner during the Sprint 5 conversation. It does not assert that a pull request exists, that GitHub has verified the Sprint 5 commits, or that any issue has been closed. The report is to be committed separately before opening the PR.

**Sprint:** Sprint 5 — API Contracts and Developer Diagnostics
**S5 foundation exception (D1):** APPROVED separately by the owner on November 5, 2026
**Complete French SMART (D2):** APPROVED separately by the owner on November 5, 2026
**English SMART:** `docs/Alycia-Sprint-5-SMART.md`; its translation was reviewed against the approved French source before the first commit
**Repository:** https://github.com/alescis-wuin/alycia
**Working branch:** `chore/api-contracts-developer-diagnostics`
**PR target:** `develop` (planned; no PR reviewed yet)
**Evidence cutoff:** November 5, 2026, 20:16:05 CET (latest owner-provided local commit evidence)
**Published software version:** None established
**Completed MVP capabilities:** 0/8

## 1. Actual scope and repository evidence

### Starting point and issue tracking

- The owner verified a clean local `develop`, with local HEAD, local `develop`, and remote `origin/develop` all at `0c786948a9b6656614ab0248b705cf8d2144d7bb` (Sprint 4 PR #26 merge). The .NET SDK reported `10.0.403`.
- The owner created the working branch from `develop` and confirmed a clean working tree.
- The owner created [parent issue #27](https://github.com/alescis-wuin/alycia/issues/27) and linked four native GitHub sub-issues: [#28](https://github.com/alescis-wuin/alycia/issues/28) (AC01–AC02), [#29](https://github.com/alescis-wuin/alycia/issues/29) (AC03–AC04), [#30](https://github.com/alescis-wuin/alycia/issues/30) (AC05–AC07), and [#31](https://github.com/alescis-wuin/alycia/issues/31) (AC08–AC09). The owner-provided sub-issue listing returned `28`, `29`, `30`, `31`.
- **SMART-only commit:** `091a23fe03183988767d532c2d32392b62178745`, November 5, 2026, 19:46:13 CET. The owner-provided output showed exactly one new file, `docs/Alycia-Sprint-5-SMART.md`, and a good local GPG signature. The message references issue #27.
- **Implementation/documentation commit:** `1647474c44a5c04b7513e4f3585fc8451bed5831`, November 5, 2026, 20:16:05 CET. The owner-provided output showed a good local GPG signature, references to #27–#31, four changed files and a clean working tree afterward.
- The elapsed calendar interval between these two commits is **29 minutes 52 seconds**. It is not a measure of active work time; no active-hours log was supplied.

### Delivered files and behavior

- `src/Alycia.Api/Alycia.Api.csproj`: added `Microsoft.AspNetCore.OpenApi` **10.0.14** and `Scalar.AspNetCore` **2.18.7**.
- `src/Alycia.Api/Program.cs`: added native OpenAPI generation and a document transformer that describes the existing `GET /healthz` with `200` and `503` `text/plain` responses. Registered OpenAPI and Scalar routes only within `app.Environment.IsDevelopment()`; configured Scalar to disable its agent and default fonts.
- `docs/Alycia-Developer-Diagnostics.md`: added instructions for local Development/Production launches, OpenAPI export, Scalar inspection, TraceId-based search, filtered JSON Lines export, and temporary-file/data hygiene.
- `README.md`: linked the diagnostic instructions from the existing API health section.
- The `MapHealthChecks("/healthz")` registration and existing Serilog request-logging configuration were left unchanged. No new business API, persistence layer, request-capture middleware, Docker service, CI/CD or advanced monitoring was added.

## 2. Acceptance criteria — evidence before PR

| Criterion | Pre-PR status | Owner-provided evidence and limitation |
|---|---|---|
| **AC01 — Accurate health endpoint OpenAPI** | **PASS** | Development `GET /openapi/v1.json` returned HTTP `200`; the saved OpenAPI 3.1.1 JSON contains only `GET /healthz`, with `200` and `503` response declarations of type `text/plain`. A real `curl -i` to `/healthz` returned HTTP `200` and the exact `Healthy` body. The real unhealthy HTTP `503` path was not deliberately triggered, as permitted by the SMART. |
| **AC02 — Valid OpenAPI JSON export** | **PASS** | The owner saved the HTTP response to `/tmp/alycia-s5-openapi-v1.json`, parsed it successfully using Python and supplied the JSON file. The document contains the generated `openapi`, `info`, `servers`, and `paths` fields, with only the real health route. No secrets appeared in the supplied document. |
| **AC03 — Functional local Scalar reference** | **PASS** | Development server logs show `/scalar` redirect `302`, `/scalar/` HTTP `200`, Scalar scripts HTTP `200`, and `/openapi/v1.json` HTTP `200`. The owner supplied a screenshot showing the `Alycia.Api | v1` reference and `GET /healthz` with the expected responses. No loading error was reported. |
| **AC04 — Development-only API documentation** | **PASS** | Under `ASPNETCORE_ENVIRONMENT=Production`, the owner observed `/healthz` HTTP `200`, `/openapi/v1.json` HTTP `404`, and `/scalar/` HTTP `404`. The Production server log confirms the environment. A configuration diff showed no changes to Compose/network exposure. The Production check captured only the health HTTP status; the exact `Healthy` body was separately observed in Development and the health implementation was unchanged. |
| **AC05 — Existing TraceId request diagnostics** | **PASS** | Python inspection of one JSON Lines file found three `GET /healthz` events. For a real event: `TraceId=f2ead8ce769b3b25fd28d512e9278404`, `RequestMethod=GET`, `RequestPath=/healthz`, `StatusCode=200`, `Elapsed=4.149699`, timestamp `2026-11-05T19:01:34.8725525Z`. Existing console logs separately showed HTTP method/path, status and duration. |
| **AC06 — Focused JSONL/JSON event export** | **PASS** | The owner filtered the existing log by the observed TraceId, wrote `/tmp/alycia-s5-trace-5qggh6zr.jsonl`, exported exactly one event, parsed the output successfully (`Valid JSONL: True`), and verified source integrity with SHA-256 before/after (`Source unchanged: True`). The supplied export contains only `@t`, `TraceId`, `RequestMethod`, `RequestPath`, `StatusCode`, and `Elapsed`. Optional JSON-array conversion was not required. |
| **AC07 — Security and existing logging preserved** | **PASS (scope-limited)** | A focused source diff showed unchanged Serilog storage, rolling, retention and correlation; no new request-body/header capture or Docker service. `git diff --exit-code -- compose.yaml Dockerfile .gitignore` returned no changes. The sampled OpenAPI JSON and filtered JSONL export contained no keys, tokens, request bodies or sensitive headers. This is a scoped review, not a proof that no future request can ever leak sensitive data. |
| **AC08 — Reproducible English docs and quality checks** | **PASS** | The English diagnostics document was copied and its SHA-256 matched the prepared source (`1c715ffe6f70aade4aec053b00628f369a41d2461fd5c5f6043c53df442c33ff`). `README.md` links to it. The owner completed the Development/Production and log/export procedures, compiled the API after both package additions, ran `make check` (`Lint passed`, **7/7** Avalonia xUnit tests), and ran `git diff --check` successfully. The final staged whitespace check also reported no errors. |
| **AC09 — Signed documents and PR verification** | **PENDING** | D1/D2 approval, the reviewed translation, the signed SMART-only commit, the signed implementation commit, and real GitHub issue references are evidenced. This factual END must still be committed in a separate signed commit before PR. The PR's actual target, links, nine checkboxes, evidence and GitHub `Verified` signatures cannot be checked until the PR exists. |

**Pre-PR tally:** AC01–AC08 **PASS (8/8)** on the recorded evidence; AC09 **PENDING**. The Sprint 5 PR and merge are not claimed as completed.

## 3. Checks actually performed by the owner

| Command or observation | Actual result | Relevance |
|---|---|---|
| `git status --short --branch`, `git rev-parse HEAD`, `git rev-parse develop`, `git ls-remote --heads origin develop`, `dotnet --version` | Local branch initially `develop`, clean; all checked refs matched `0c786948...`; SDK `10.0.403`. | Starting baseline. |
| `git switch -c chore/api-contracts-developer-diagnostics develop` and `git status --short --branch` | Branch created; working tree clean. | Sprint branch prerequisite. |
| `gh issue create` and native `gh api .../issues/27/sub_issues` operations | Parent #27 and issues #28–#31 created; final sub-issue listing showed all four IDs. | Issue traceability. |
| `git diff --cached --check`, first `git commit -S`, `git log -1 --show-signature` | SMART-only commit `091a23f`, good GPG signature, no staged whitespace errors. | AC09 documentary prerequisite. |
| `dotnet add ... Microsoft.AspNetCore.OpenApi --version 10.0.14`, `dotnet add ... Scalar.AspNetCore --version 2.18.7` | Both package restores completed successfully and reported .NET compatibility. | AC01–AC04. |
| `dotnet build src/Alycia.Api/Alycia.Api.csproj` | Successful after OpenAPI changes (2.5 s) and after Scalar changes (1.5 s). | AC01–AC04, AC08. |
| Development `dotnet run --no-build --no-launch-profile ... -- --urls http://127.0.0.1:5187` and `curl` | `/healthz` HTTP `200`, `Content-Type: text/plain`, body `Healthy`; `/openapi/v1.json` HTTP `200`. | AC01–AC02. |
| Python parsing of `/tmp/alycia-s5-openapi-v1.json` | OpenAPI `3.1.1`; `paths=['/healthz']`; `200` and `503` both declare `text/plain`. Actual file supplied. | AC01–AC02. |
| Local browser visit to Scalar and accompanying server log/screenshot | Scalar UI displayed the real operation; assets and OpenAPI JSON loaded with HTTP `200`. | AC03. |
| Production launch using `ASPNETCORE_ENVIRONMENT=Production` on loopback port `5188`; `curl` for three paths | `/healthz: 200`, `/openapi/v1.json: 404`, `/scalar/: 404`. API shut down afterward. | AC04. |
| Python scan of `src/Alycia.Api/logs/alycia-*.jsonl` | One log file, three matching health requests; selected event contained timestamp, TraceId, method, path, status and elapsed duration. | AC05. |
| Python filtered JSONL export, parse check and source SHA-256 comparison | One exported event; valid JSONL; source unchanged; restricted field set. Actual file supplied. | AC06–AC07. |
| `git diff --exit-code -- compose.yaml Dockerfile .gitignore` and focused code diff | No changes to the listed configuration files; added only OpenAPI/Scalar integration to the API code. | AC07. |
| `sha256sum docs/Alycia-Developer-Diagnostics.md` | Matched the prepared document hash above. | AC08. |
| `make check`, `git diff --check` | Lint passed; 7 tests passed, 0 failed, 0 skipped; no diff whitespace findings. | AC08. |
| `git diff --cached --check`, `git diff --cached --stat`, second `git commit -S`, `git status --short --branch`, `git log -1 --show-signature` | Four expected files in implementation commit `1647474`; good local GPG signature; working tree clean after commit. | Source/commit traceability. |

No failures requiring fixes were reported during the implementation checks. A pre-existing merge commit could not be locally authenticated because its public key was unavailable, but that result concerns the earlier Sprint 4 merge; both **new** Sprint 5 commits were verified locally with good GPG signatures.

## 4. AC09 pre-PR documentary checklist

- [x] D1 — Targeted Sprint 5 foundation exception explicitly approved by the owner.
- [x] D2 — Complete French Sprint 5 SMART explicitly approved by the owner.
- [x] English SMART translation reviewed against the approved French source before the first sprint commit.
- [x] First signed commit contains only `docs/Alycia-Sprint-5-SMART.md` (`091a23f`).
- [x] Actual parent and sub-issue IDs/URLs established; relevant issue references included in the signed commits.
- [x] AC01–AC08 supported by the owner-provided evidence described above.
- [ ] This factual English END is added in its own signed commit before PR. **Not yet completed at this evidence cutoff.**
- [ ] PR into `develop`, direct SMART/END and issue links, separate AC01–AC09 checkboxes, evidence and GitHub `Verified` signatures. **PENDING until actual PR review.**

## 5. Remaining risks, limitations and exclusions

- A live unhealthy `503` was not induced. The approved SMART explicitly accepts a documented 503 response without requiring artificial failure when only the healthy case is feasible.
- The exact Production health response body was not captured by the three-code-status test; `Healthy` was observed under Development and the existing health-check handler is unchanged.
- The available OpenAPI/JSONL samples and focused configuration diff support the privacy conclusion only for the inspected scope; they do not constitute exhaustive security testing.
- No Docker Compose runtime regression check was required or reported for these focused local documentation changes. No Windows/MAUI tests were performed.
- Both new commits have good **local** GPG signatures. Their GitHub `Verified` statuses cannot be claimed before push and inspection.
- No owner-provided evidence establishes a PR, a merge, closed Sprint 5 issues, or a published software version.
- Outside scope: new business endpoints, authentication, EF Core persistence, request database, unfiltered captures, custom query UI, monitoring stack, CI/CD and GitHub Actions.
- The existing `/healthz` remains a technical plain-text health check outside the application JSON envelope, with no PostgreSQL probe.
- Active work time is unknown; commit timestamps establish only calendar elapsed time.

## 6. Handoff for PR and administrative closure

1. Add and commit **only this factual END file** in a third signed Sprint 5 commit. Record the resulting commit SHA and local GPG verification in the PR; this report cannot contain its own commit SHA.
2. Push the Sprint 5 branch, then open a PR targeting `develop`. Include direct links to the versioned SMART and END files, issue #27 and sub-issues #28–#31, and an evidence-backed separate checkbox for AC01–AC09.
3. **After PR opening and before merge**, verify its base branch, document/issue links, checklist, the evidence associated with each checked criterion, and the GitHub `Verified` status of each Sprint 5 commit. Only then may **AC09** be marked **PASS** in the PR.
4. Merge only after all required criteria pass and the owner authorizes it. Once merged, manually close satisfied sub-issues and then parent #27. Do not rewrite this pre-PR report to describe later events.

**Pre-PR status at evidence cutoff:** **AC01–AC08 PASS; AC09 PENDING; no Sprint 5 PR or merge verified.**
