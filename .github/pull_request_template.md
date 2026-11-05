# Sprint PR — [Sprint N — English Title]

## SMART sheet

- Sprint: `Sprint N — English Title`
- SMART sheet: [Open approved sheet](REPLACE_WITH_REAL_LINK_TO_DOCS_ALYCIA_SPRINT_N_SMART)
- END report: [Open pre-merge report](REPLACE_WITH_REAL_LINK_TO_DOCS_ALYCIA_SPRINT_N_END)
- Source branch: `REPLACE_WITH_SPRINT_BRANCH`
- Target branch: `develop`

## Linked issues

- Parent: `#REPLACE_WITH_REAL_ID` — `REPLACE_WITH_REAL_URL`
- Deliverable sub-issues: `#REPLACE_WITH_REAL_IDS_AND_URLS`

## Acceptance criteria (replace these examples with EVERY ACxx from the sheet)

- [ ] AC01 — REPLACE_WITH_EXACT_CRITERION_AND_EVIDENCE
- [ ] AC02 — REPLACE_WITH_EXACT_CRITERION_AND_EVIDENCE

Do not check a criterion without real evidence. Complete the entire ACxx list from the versioned SMART sheet before merging. Post-merge issue closure is a separate administrative step, not a pre-merge checkbox.

## Signed commits and sprint timeline

- [ ] All new sprint commits are cryptographically signed and verified on GitHub.
- Initial SMART-only commit SHA / committer timestamp: REPLACE.
- END report commit SHA / committer timestamp: REPLACE (recorded here **after** signing and committing the report).
- Calendar interval between boundary commits: REPLACE_OR_UNKNOWN (not active work time).
- Active work time: REPLACE_IF_MEASURED_OR_WRITE_NOT_MEASURED.

## Verification evidence

| Criterion | Command, manual check, or source | Observed result | Evidence / limitations |
|---|---|---|---|
| AC01 | REPLACE | REPLACE | REPLACE |

## Scope and risks

- In scope: REPLACE_WITH_SHORT_SUMMARY
- Excluded: REPLACE_WITH_SHORT_SUMMARY
- Remaining risks or unresolved issues: REPLACE_OR_WRITE_NONE_IF_VERIFIED

## Final review

- [ ] The English SMART sheet faithfully translates the approved source, preserving all ACxx criteria and scope.
- [ ] The END report is in English and uses only verified evidence.
- [ ] Both SMART and END links point to the correct versioned files in `docs/`.
- [ ] Every ACxx has a checkable row and genuine supporting evidence.
- [ ] No secrets, tokens, or private data are disclosed in logs or PR evidence.
- [ ] All mandatory pre-merge checks are satisfied.

**After merge:** record the merge and issue closures in GitHub; the END report is a pre-merge snapshot. Close only verified sub-issues, then the parent issue when its completion conditions are satisfied. GitHub will not automatically close these issues for a PR targeting `develop` when `main` is the default branch.

**Sprint 2 onboarding exception:** this pull request template is a new Sprint 2 deliverable. Add `.github/pull_request_template.md` in a normal signed Sprint 2 working commit (not the first SMART-only commit). When opening the initial Sprint 2 PR, paste and complete this template manually if GitHub does not offer it yet because it has not reached the default `main` branch. Do not push it directly to a protected branch just to activate it.
