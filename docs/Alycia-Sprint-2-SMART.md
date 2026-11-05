# Alycia — Sprint 2: SMART Specification

**Sprint 2 — GitHub Repository and Governance Setup**  
**Document revision:** 4 (November 5, 2026)  
**Status:** APPROVED — French SMART sheet revision 4 approved by the owner on November 5, 2026  
**Context:** amendment to the Sprint 0 plan; no additional MVP business capability  
**References:** Alycia Charter revision 1.7, § 7; Consolidated Scoping revision 10, §§ 13–14; approved French Sprint 1 SMART sheet for historical acceptance criteria  
**Planned branch:** `setup/repository-governance`, created from a **local** `develop` branch  
**PR target:** `develop`; **software release:** none  
**Indicative duration:** focused sessions of 30–90 minutes; continue if the criteria are not met.  
**Startup archive:** contains the French source and its English translation; the archive itself did not constitute approval. Approval was explicitly granted by the owner after review.

## 1. SMART Objective

Establish reproducible GitHub governance from the **existing local Alycia repository**: a remote origin, three permanent branches, branch protection compatible with a sole contributor, labels, issues linked to SMART criteria, versioned SMART/END documents, signed commits, and a controlled PR into `develop`. Honestly backfill the missing Sprint 1 issues without rewriting its history.

**Observable result:** the owner can demonstrate the correct remote repository, its effective branches and rules, linked issues for both sprints, both SMART and END documents present in Git, signed commits, and a PR merged into `develop` with its checklist and evidence. Issues are only closed when supported by evidence.

## 2. Initial State and Prerequisites

- Verify the **actual** completion of Sprint 1 from the local repository and available evidence; do not assume closure.
- Inspect the local Git repository, history and current branch; preserve existing work and confirm a clean or otherwise safeguarded working tree before sensitive operations.
- Ensure Git, `gh` and GitHub authentication work, with permission to create and configure the new repository.
- Verify the intended GitHub owner, repository visibility (public/private), and any existing `origin` before creating anything; **do not overwrite existing configuration**.
- Confirm that required protections are effectively available under the GitHub account and repository permissions. If not, request a decision rather than marking `AC04` passed.
- Obtain approval of the Sprint 2 SMART sheet **in French**, then **translate it fully and faithfully into English** and compare both versions before adding anything to `docs/` or making the first commit. Preserve the meaning, objectives, `ACxx`, scope, tests, exclusions and issue plan.

### Consistent Bootstrap Sequence

To create the Sprint 2 branch *from `develop`* when `develop` may not yet exist, first inspect the history, then establish **local** `develop` from an appropriate verified existing commit. Create `setup/repository-governance` from that reference. **This preliminary local operation is neither a push nor a work commit.**

After verifying the faithful English translation, the **first signed (`git commit -S`) Sprint 2 commit** must contain **only** `docs/Alycia-Sprint-2-SMART.md`. The GPG or SSH signing key and `git commit -S` must be functional **before the first commit**. The remote repository and issues may be created afterward; the first commit includes `Sprint: 2` but no fabricated issue references. The initial pushes needed to establish and protect all three remote branches may occur before the protection rules are activated, solely for this first setup. Enabling "Require signed commits" must account for potentially unsigned historical commits without rewriting them. Creating `main` does not automatically publish a software version.

## 3. Included Deliverables

1. A new Alycia GitHub repository connected to the **correct** existing local repository through `origin`, without overwriting an existing remote.
2. Remote `main` (default branch), `testing` and `develop`, all effectively protected: PRs required, force-pushes and deletion prohibited, and no required third-party approvals or CI.
3. The working branch `setup/repository-governance`, derived from `develop`, with verifiable history.
4. Reusable GitHub labels: `type:sprint`, `type:task`, `area:git`, `area:infrastructure`, `area:api`, `area:observability`, `area:docs`, `status:retrospective` (create only labels that are absent).
5. A Sprint 2 parent issue and deliverable sub-issues, actually linked, with a mapping to `AC01`–`AC10`.
6. A Sprint 1 parent issue and four retrospective sub-issues, created if absent, using the titles, descriptions, labels and `AC01`–`AC10` from the approved French Sprint 1 sheet. Their outcomes rely on existing evidence.
7. English SMART documents versioned under `docs/` for Sprints 1 and 2, and English END reports for both (Sprint 1 retrospectively), linked from issues and the PR.
8. An English `.github/pull_request_template.md` containing SMART/END links, issue references, an `ACxx` checklist and evidence.
9. A signed pre-PR preparation commit adding `docs/Alycia-Sprint-2-END.md` as a factual Sprint 2 report (criteria, read-only commands, observed outputs and limitations). The END commit SHA and the PR URL cannot appear in that same commit; record them in the PR afterward.
10. A Sprint 2 PR into `develop`, with genuine checklist and evidence, merged with the owner's authorization; satisfied issues are closed manually after merging. All new Sprint 2 commits use `-S`, with GitHub verification of their signatures.

**Important:** a PR template added on a sprint branch is not offered automatically by GitHub until it is available on the default `main` branch. For the initial Sprint 2 PR, manually fill in the template body if necessary. Verify automatic template availability on a later authorized promotion to `main`, **without automatically publishing a software version**.

### Signature Verification and Report Timing

- Before the first Sprint 2 commit, establish whether Git uses an **SSH or GPG** signing key. Use `git commit -S` (not `git commit -s`). If signing cannot work, starting the sprint is blocked until configuration or an explicit decision.
- Do not rewrite historical Sprint 1 commits even if they are unsigned. Test whether GitHub's "Require signed commits" rule can apply to new contributions on all target branches. If inherited unsigned history prevents protection or merging, **do not mark `AC04` or `AC10` passed**; explain the conflict before any history rewrite.
- `docs/Alycia-Sprint-2-END.md` is a **pre-PR snapshot**. Some criteria can be verified only after opening the PR or pushing the END commit: mark them `PENDING` rather than `PASS` until evidence is available in the PR. The report may explicitly note that no PR number exists yet and that issue closure is pending. After committing the report, record the final SHA and post-merge operations **in the PR and issues**, rather than backdating or altering the pre-PR report.

## 4. Excluded Scope

- Changes to business code, the API or the .NET solution.
- Avalonia, MAUI, EF Core, authentication, RAG and the vault.
- CI/CD, GitHub Actions, requirements for external approval, or mandatory automated checks that do not exist.
- Software releases, `vX.Y.Z` tags, or ordinary promotion into `testing` or `main`.
- Rewriting, amending or backdating Sprint 1 commits to fabricate issue links.
- Closing issues whose acceptance criteria lack evidence.
- Treating Git commit timestamps alone as an automated measurement of active work hours.

## 5. Acceptance Criteria Before Merge

| ID | Verifiable result | Expected evidence |
|---|---|---|
| **AC01** | Local history and branches inspected; local `develop` and correctly derived Sprint 2 branch established. | Observed branches, SHAs and ancestry. |
| **AC02** | Alycia remote GitHub repository created and `origin` linked to the correct repository. | Actual URL and `git remote -v`, `gh repo view`. |
| **AC03** | `main`, `testing`, `develop` exist on GitHub; `main` is the default. | Actual branch list and GitHub default branch. |
| **AC04** | The three protection rules actually apply and remain usable without a third party or CI. | Observed effective settings and confirmed plan/permission constraints. |
| **AC05** | Labels, Sprint 2 parent issue and linked sub-issues exist, with AC mapping. | Real issue numbers, URLs, labels and parent/child relationships. |
| **AC06** | Sprint 1 issues backfilled, with historical evidence and real status recorded; no fabrication. | URLs, context comment and existing evidence references. |
| **AC07** | Sprint 1/2 SMART sheets **faithfully translated** into English from their approved French sources before addition; English END reports for Sprints 1/2; signed first Sprint 2 commit containing only its SMART sheet. | Comparison of criteria and paths; verifiable Git history. |
| **AC08** | PR template available on the working branch; actual Sprint 2 PR includes SMART/END links, issues and `AC01`–`AC10` checkboxes. | `.github/` file and the actual PR description. |
| **AC09** | A signed commit before the PR adds the Sprint 2 END report **in English**, using real read-only command outputs; calendar duration traceable between both boundary commits. | Pre-merge report, commands and results; first SHA in report, last SHA in PR. |
| **AC10** | All new Sprint 2 commits signed using `-S` and verifiable on GitHub; signed commits required on all three protected branches wherever compatible with the observed history. | `git log --show-signature`, GitHub "Verified" status and effective protection settings. |

**Administrative closure after merging (excluded from the pre-merge checklist):** the Sprint 2 PR is actually merged into `develop`; satisfied sub-issues are closed, and the parent issue closes only after all the sprint criteria are proven. Sprint 1 issues close only if their own `AC01`–`AC10` are actually supported. Missing evidence keeps an issue open and is disclosed; a sprint must not be called complete while any mandatory criterion remains unsatisfied.

## 6. Issues to Create (Titles, Descriptions and Labels)

**Sprint 2 parent issue:**

- **Exact title:** `Sprint 2 — GitHub Repository and Governance Setup`
- **English description:** `Establish the GitHub remote and protected main/testing/develop branches, link sprint deliverables to issues, version SMART and END reports directly in docs/, and validate the first pull request to develop. Retrospectively document Sprint 1 issues without rewriting history. Track AC01–AC10 and record evidence before merging.`
- **Labels:** `type:sprint`, `area:git`.
- **SMART link:** `docs/Alycia-Sprint-2-SMART.md` (record the actual URL after pushing).
- **Real number/URL:** complete only after issue creation; never invent.

**Sprint 2 deliverable sub-issues:**

| Exact title | English description to use | Labels | AC |
|---|---|---|---|
| `[S2] Establish remote and permanent branches` | `Audit the existing Git history, establish the local sprint branch, create the GitHub repository, configure origin, and establish main, testing, and develop without overwriting history.` | `type:task`, `area:git` | `AC01`–`AC03` |
| `[S2] Enforce branch protection rules` | `Verify that the protected branches require PRs, reject deletion and force pushes, and require verified signed commits where compatible with the audited history; no required third-party reviews or CI.` | `type:task`, `area:git` | `AC04`, `AC10` |
| `[S2] Create labels and current sprint issues` | `Create the agreed label taxonomy and connect the Sprint 2 parent issue and deliverable sub-issues to AC01–AC10.` | `type:task`, `area:git` | `AC05` |
| `[S2] Backfill Sprint 1 issue tracking` | `Create the Sprint 1 parent issue and deliverable sub-issues using the approved sheet; capture genuine prior evidence and explicitly mark missing proofs. Do not backdate records.` | `type:task`, `area:docs`, `status:retrospective` | `AC06` |
| `[S2] Version sprint sheets and PR template` | `Add English SMART and END reports directly in docs/ and a reusable pull_request_template.md; sign the first Sprint 2 commit containing only its SMART sheet, then sign the END report commit with real evidence.` | `type:task`, `area:docs` | `AC07`–`AC09` |

The Sprint 1 issues to be created are described in the **approved French Sprint 1 sheet**: parent issue and four sub-issues, including their texts and labels. New S1 and S2 issues must not receive arbitrary numbers in these documents.

## 7. Implementation Plan

1. Inspect the Sprint 1 state (including what was truly tested), the local repository and any remotes; overwrite nothing.
2. Obtain approval of the French SMART sheet, **verify the faithful English translation**, then check the SSH/GPG key and signing, establish local `develop` and `setup/repository-governance`, and **make the first `git commit -S` containing only `docs/Alycia-Sprint-2-SMART.md`**. Record its initial SHA.
3. With the owner's authorization for remote operations, create the GitHub repository and `origin`, bootstrap `main`, `testing` and `develop`, then configure and inspect their protection rules.
4. Create labels and the Sprint 2 parent/sub-issues, then backfill those of Sprint 1; record real numbers, URLs, AC mapping and evidence.
5. Add the English Sprint 1 SMART and END reports, the PR template and governance information, without changing business code.
6. Verify criteria observable **before the PR**, fix only failed checks and explicitly note items that cannot yet be verified (notably `AC08` and GitHub verification of the END commit's signature). Then **write** `docs/Alycia-Sprint-2-END.md` **in English** from real evidence and add it in a **signed commit** before opening the PR. Record its SHA.
7. Push the signed commits, confirm GitHub "Verified" status, open the Sprint 2 PR targeting `develop`, add SMART/END links and one AC checkbox with evidence for each criterion, check the items that were not observable before the PR (including `AC08`), then merge only on the owner's decision.
8. Close satisfied issues after merging and record merge/closure references on GitHub; leave unsupported issues open. Do not backdate or rewrite the pre-PR END sheet.

## 8. Tests and Checks

- **Unit tests:** none; no business logic is created in this sprint.
- **Targeted integration:** read-only Git/GitHub queries confirming `origin`, branches, effective protections, parent/sub-issue links, PR and merge.
- **Manual:** compare issue descriptions to the sheet, inspect GitHub rules, checklist and evidence; check the initial and final commit timestamps.
- **Excluded testing:** .NET builds, API requests, PostgreSQL connectivity, UI, application security and performance. The Sprint 1 outcome is assessed from previously available evidence only; do not rerun its entire suite without a concrete reason.

## 9. Timestamps and Duration

Author (`%aI`) and commit (`%cI`) timestamps can be read with `git show -s --format='%H%nAuthor: %aI%nCommit: %cI' <sha>`. The interval between the initial and final commits provides an **approximate calendar duration**, not active work time. Rebases, amendments, squashes, clock changes, breaks and work performed before the first commit can affect it.

To measure actual work, optionally maintain a session log recording start time, end time, breaks and time worked. **Sprint 1:** do not rewrite old commits to simulate this measurement.

## 10. Closure Condition

All ten acceptance criteria are proven, required protections actually exist, traceability information is authentic, the PR has been merged into `develop`, and satisfied issues have been closed according to the procedure. No Alycia software version is published as a consequence of this sprint alone. No software test or behavior may be presented as verified without evidence.
