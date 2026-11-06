# Alycia — Sprint 7: Secure Owner Initialization — Pre-PR End Report

> **Pre-PR evidence snapshot.** This report records observations available before the END commit and pull request. It does not declare Sprint 7 complete. AC07 still requires a targeted runtime envelope check; AC11 depends on the subsequent pull-request review. Do not change either to PASS without the missing evidence.

- **Sprint:** Sprint 7 — Secure Owner Initialization.
- **Specification:** `docs/Alycia-Sprint-7-SMART.md`; complete French SMART and faithful English translation approved and frozen by the owner on November 5, 2026, including password policy B and the incorrect-secret counter/regeneration refinement.
- **Repository:** https://github.com/alescis-wuin/alycia.
- **Working branch / intended target:** `feature/secure-owner-initialization` → `develop`.
- **Evidence cutoff:** November 6, 2026, after the two locally signed implementation commits (signature output at 02:07 CET), before this END is committed or a Sprint 7 PR is opened.
- **Baseline:** Sprint 6 merge PR #38; local `develop` at `8179f9d53ae2fe0a41525c2ec4a43ba272a1763b` when the S7 branch was prepared. GitHub read-only inspection confirmed S6 issues #33–#37 closed.
- **Published release:** none evidenced. **Completed MVP capabilities:** 0/8 at the last documented snapshot; S7 does not complete Authentication/Sessions.

## 1. Delivered scope

- Introduced the first Identity Domain, Application and Infrastructure projects with an owner `User`, a normalized ASCII login identifier, a dedicated user repository, single-owner persistence, an `IdentityDbContext` and owned EF Core migrations.
- Implemented the approved password policy: 15–128 printable Unicode code points after NFC normalization; significant spaces preserved; no character-category rule; a documented local common/compromised-password blocklist; and `PasswordHasher<User>` persistence instead of plaintext.
- Added `Workspace.OwnerUserId` after S6, deterministic explicit migrations through `make migrate`, and shared PostgreSQL transaction coordination outside the business modules. Existing Workspaces are attached to the sole owner or the transaction is rolled back.
- Added an administrative `bootstrap generate` command and durable one-use bootstrap state: a 256-bit secret, 15-minute validity, at most five incorrect-secret presentations per issuance, safe regeneration after expiry/lockout, and permanent closure after owner initialization.
- Added a loopback-only bootstrap HTTP API with application JSON envelopes and actual TCP-peer validation. The inherited S6 anonymous Workspace route exception was not carried forward.
- Added an Avalonia first-run form with the secret, login, password and confirmation fields, server/client validation, backend state handling and no S8 authenticated session.
- Added English bootstrap, migration and blocklist guides, unit tests, isolated PostgreSQL integration tests, and a Docker-peer network-denial test.

**Out of scope:** normal sign-in/sign-out, bearer tokens, device sessions, public signup, a second owner, credential recovery, MAUI/Windows parity in this Linux slice, the Secrets vault, CI/CD, releases and promotion of `testing` or `main`.

## 2. Git and issue evidence

| Item | Observed evidence | Limit |
|---|---|---|
| Sprint 6 prerequisite | PR #38 merged; `develop` baseline `8179f9d`; issues #33–#37 confirmed closed. | Observed before S7 implementation; no additional S6 changes claimed. |
| SMART-only first commit | `0f675cf63af79142479e5aee567e113b48e36980`, adding only `docs/Alycia-Sprint-7-SMART.md`; pushed previously. | Recheck GitHub `Verified` during S7 PR review. |
| Backend implementation | `aa7daa1`, 50 changed files, GPG `git verify-commit` reported a good signature on November 6, 2026. | Local signature, not proof of GitHub `Verified`. |
| Avalonia implementation | `f4f8cd0`, 4 changed files, GPG `git verify-commit` reported a good signature on November 6, 2026. | Local signature, not proof of GitHub `Verified`. |
| Local worktree | `git status --short --branch`: `feature/secure-owner-initialization...origin/feature/secure-owner-initialization [ahead 2]`, no file changes. | State before adding this report; the implementation commits had **not yet been pushed**. |
| END commit | **PENDING:** commit this factual file **alone**, with `git commit -S`, after closing AC07. | Its SHA belongs in the PR, not in this pre-commit report. |
| Sprint 7 PR, merge, GitHub signatures | **PENDING.** | No PR number, remote verification or merge is asserted. |
| Issues | Parent [#39](https://github.com/alescis-wuin/alycia/issues/39); deliverables [#40](https://github.com/alescis-wuin/alycia/issues/40), [#41](https://github.com/alescis-wuin/alycia/issues/41), [#42](https://github.com/alescis-wuin/alycia/issues/42), [#43](https://github.com/alescis-wuin/alycia/issues/43), [#44](https://github.com/alescis-wuin/alycia/issues/44). All open at the read-only inspection. | Close satisfied child issues and then the parent **manually after merge**. |

## 3. AC01–AC11 — Pre-PR assessment

| AC | Status | Observed evidence and limit |
|---|---|---|
| **AC01** — Owner identity and username | **PASS** | Identity unit tests for invalid logins, 3/64/65-character limits, case normalization and nonempty GUID; single-owner EF mapping; isolated PostgreSQL concurrency left exactly one owner. |
| **AC02** — Password policy and storage | **PASS** | Identity policy tests cover 14/15/128/129 Unicode-code-point limits, NFC, spaces, control characters, no composition rule and local blocked values; verifier test uses `PasswordHasher<User>`. Source and targeted secret/log review found no intentional plaintext persistence. The finite local list is not a complete breach corpus. |
| **AC03** — Migrations | **PASS** | Owner applied the S6 schema to an isolated database, seeded historical Workspaces, ran ordered S7 migrations and repeated `make migrate` successfully; module migrations remain explicit rather than running on API startup. Shared business transaction does not make cross-module schema migrations atomic. |
| **AC04** — Atomic owner/backfill | **PASS** | Isolated integration tests exercise rollback, retry and concurrent initialization. In the manual database, one owner was created and both historical Workspaces were linked (`owners=1`, `workspaces=2`, `attached=2`, `distinct_owners=1`). |
| **AC05** — Temporary secret and retry policy | **PASS** | Dedicated PostgreSQL tests cover 64-hex-character secret generation, expiry, five incorrect presentations only, restart-persistent attempts, regeneration/reset and predecessor invalidation, rollback and consumption. Manual CLI rejected reissuance after initialization. Secret hygiene was screened heuristically, not exhaustively proven. |
| **AC06** — Local access and permanent closure | **PASS** | Native Production API loopback flow succeeded; Docker peer with spoofed headers was denied HTTP 403. After native API restart, owner status remained initialized; the CLI rejected regeneration and a second POST was rejected HTTP 409 `OWNER_ALREADY_INITIALIZED`. No external/LAN bootstrap access was enabled. |
| **AC07** — API envelope and `/healthz` | **PENDING** | Actual `/healthz` HTTP 200, bootstrap status and post-init HTTP 409/code were observed. Endpoint source creates `success,status,code,message,data,errors,traceId,timestamp`. A targeted **runtime** assertion of the full envelope, trace ID, sanitized error payload and technical health response has not yet been supplied; see the finalization procedure. |
| **AC08** — Avalonia first-run experience | **PASS** | Owner supplied screenshots of Linux initial-owner UI, a password-validation error and successful initialization with sensitive fields cleared and another initialization disabled. Client tests cover confirmation, Unicode bounds, denial, unavailable/timeout, malformed envelopes and redirects. The observation is not a recorded MAUI test. |
| **AC09** — End-to-end and security boundary | **PASS** | The isolated `alycia_s7_test_manual` database started with 0 owners and 2 unowned Workspaces; Avalonia → native API → PostgreSQL produced 1 owner and 2/2 attachments. After restart, state persisted, secret was consumed and second initialization rejected. Source review finds no S6 anonymous Workspace route mapping in S7; a direct runtime 404 check is included with the remaining HTTP verification. |
| **AC10** — Tests, quality and documentation | **PASS** | Final owner-run `make check`: lint passed; `dotnet test Alycia.slnx` reported **96 passed, 0 failed, 0 skipped**. The PostgreSQL suite was executed **separately** with **8/8 passing**, not counted within those 96 solution tests. Native API build and live Avalonia flow succeeded; English usage/migration/blocklist documentation is present. Local staged-content and accessible-log heuristic scan passed; this is not a comprehensive secret-leak audit. |
| **AC11** — Pre-PR documents and PR verification | **PENDING** | The French/English SMART is approved; first SMART-only commit and two implementation commits exist with local signing evidence. This END is awaiting its own signed commit. PR base, document/issue links, every AC checkbox and GitHub `Verified` signatures can only be checked after the PR exists and before merge. |

**Pre-PR tally at this evidence cutoff: 9 PASS / 2 PENDING / 0 FAIL (AC07 and AC11).** Do not count unperformed follow-up checks as already passed. After AC07 is verified, update its row and the tally to **10 PASS / 1 PENDING** before signing the END commit. AC11 must remain PENDING in this versioned pre-PR report.

## 4. Commands and observations reported by the owner

| Check or source | Actual reported result | AC |
|---|---|---|
| `ALYCIA_MIGRATE_DATABASE=alycia_s7_test_manual make migrate` after an S6 schema and two inserted Workspaces | Migration succeeded; database then held 0 owners and 2 unowned Workspaces. Earlier isolated migration checks included a successful repeated invocation. | AC03–AC04 |
| Separate PostgreSQL integration-suite execution | 8/8 integration tests passed as previously reported by the owner; rollback and concurrency scenarios included. | AC01, AC03–AC06, AC10 |
| `bash scripts/test-s7-bootstrap-peer.sh` with an API container available | HTTP 403 for a Docker peer with forged `Host` and `X-Forwarded-For` headers. | AC06 |
| Native API and real Avalonia first-run on the isolated database | Owner UI showed successful initialization. After closing Avalonia, API returned `initialized: true`; SQL returned `owners=1, workspaces=2, attached=2, distinct_owners=1`. | AC04, AC08–AC09 |
| Administrative reissuance and native API restart, followed by a second bootstrap POST | CLI reissuance rejected; initialized state persisted; second POST returned HTTP 409 with `OWNER_ALREADY_INITIALIZED`. SQL returned `owners=1, workspaces=2, attached=2, consumed_secrets=1`. | AC05–AC06, AC09 |
| `make check` on the 54-file working diff | `bash scripts/quality.sh lint`: `Lint passed.`; `dotnet test Alycia.slnx`: **96 passed, 0 failed, 0 skipped**. | AC01–AC02, AC08, AC10 |
| Heuristic security screening and manual diff review | Automated scan reported OK; owner reviewed the staged diff and confirmed `oui`; exactly two GPG-signed implementation commits followed. | AC02, AC05–AC06, AC10 |
| `git verify-commit HEAD` after each implementation commit | GPG reported a good signature for `aa7daa1` and `f4f8cd0`; `git status` reported a clean worktree and branch ahead by two commits. | AC11 prerequisites |

No actual full runtime envelope output was reported at the cutoff. No secret, password, raw request body or credentials are reproduced here.

## 5. Security and evidence boundaries

- The successful end-to-end exercise was performed against **`alycia_s7_test_manual`**, not the owner's normal `alycia` database. The isolated database contained test records only; no deletion of the normal database is claimed. The normal database's later state must not be inferred from this report.
- A 127.0.0.1 host port binding alone is insufficient against Docker peers. The application explicitly inspects the actual TCP peer; the observed Docker-peer HTTP 403 is the network evidence for this scenario. No broad network penetration assessment is claimed.
- The documented local password list is deliberately finite; it does not guarantee that every compromised password is rejected. No external password-disclosure service was used.
- The positive bootstrap creates **no** normal login session. Workspaces business endpoints are not made anonymously available by S7. Authentication S8/S9 and Windows/MAUI parity remain future work.
- Unit/solution tests and isolated PostgreSQL integration tests are distinct commands and distinct counts. No CI/CD, production load test, disaster-recovery assessment or full automated UI coverage is claimed.
- The available secret-screening scripts are heuristic. Source inspection and limited log checks do not establish universal absence of sensitive data in every possible diagnostic path.
- No active-work time log was supplied. Commit timestamps indicate calendar events, not measured working hours.

## 6. Required completion sequence

1. Complete the targeted non-destructive HTTP envelope/technical health/retired Workspace route check on the initialized **isolated** database. If successful, change AC07 to PASS and the tally to `10 PASS / 1 PENDING / 0 FAIL`; if unsuccessful, fix the actual defect and rerun the affected checks before editing this report.
2. Review and stage **only** `docs/Alycia-Sprint-7-END.md`, create a separate `git commit -S` on the S7 branch and verify its signature locally. Do not add the draft PR body or a local verification script to the repository unless separately reviewed and deliberately approved.
3. Owner pushes the sprint branch and opens a PR to `develop`, linking the real parent #39, children #40–#44, signed SMART and END. Record the END commit SHA in the PR, not retroactively in this report.
4. In the actual PR, verify the base/head, document and issue links, meaningful proof and a checkbox for **each of AC01–AC11**, and GitHub `Verified` status for **all four** S7 commits. Only after the PR-specific checks pass may AC11 be marked PASS **in the PR**; the signed pre-PR END remains an unchanged historical snapshot.
5. Merge only with all mandatory criteria verified. Verify the resulting merge in `develop`, then the owner manually closes satisfied child issues #40–#44 and parent #39. Do not assume automatic closure from a PR targeting `develop` while `main` is the default branch.
6. Do not promote `testing` or `main`, create a release, or mark an entire MVP capability complete as a consequence of the S7 merge.

**Status at report authoring:** technically implemented and locally signed; AC07 runtime check, signed END, PR review, merge and manual issue closure remain outstanding. Sprint 7 is **not yet closed**.
