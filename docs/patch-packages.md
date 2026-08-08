# Verified patch packages

Alicia patch packages automate the complete local workflow around a code
change. The package is treated as an auditable build input rather than an
unstructured archive.

## Apply a package

Place the ZIP archive in `~/Téléchargements`, then run:

```bash
make patch PATCH=alicia-live-result-preview-01
```

The runner performs these operations:

1. locates the requested ZIP archive;
2. rejects unsafe ZIP paths, links, encryption, duplicates, and oversized data;
3. extracts the package into an isolated directory below `logs/patches/`;
4. validates `manifest.json`, every declared file, extensions, UTF-8 content,
   JSON syntax, per-file SHA-256 values, and `SHA256SUMS`;
5. verifies the repository root, start branch, required paths, exact Git status,
   and expected modification count;
6. records `git diff --check`, `git status --short`, and diff statistics;
7. creates or verifies the target work branch;
8. executes the package `apply.sh` entrypoint;
9. verifies the exact post-application status and changed paths;
10. executes every validation command even when an earlier validation fails;
11. optionally runs the application and asks for explicit acceptance;
12. stages only manifest-authorized paths and validates staged-file safety;
13. generates the commit message from manifest header, body, and footer fields;
14. creates and verifies the signed commit;
15. runs post-commit validation and verifies the final repository state.

A failure preserves the repository state for investigation. The runner does not
use `git reset --hard` and does not delete user work.

## Logs

Every execution creates:

```text
logs/patches/YYYYMMDD-HHMMSS-<patch-name>/
├── console.log
├── events.jsonl
├── summary.json
├── commands/
├── snapshots/
└── work/
```

Console messages use this structure:

```text
[time][PATCH][step/total][progress][stage][level][elapsed] message
```

Colors are enabled only for an interactive terminal. Set `NO_COLOR=1` to
disable them. Command output is prefixed and stored in a dedicated file.

## Manifest structure

The manifest records:

- patch name and description;
- project and repository identity;
- expected start and end branches;
- branch type, name, creation policy, and base reference;
- every package file with kind, non-empty requirement, and SHA-256 checksum;
- exact or subset-based Git status before and after application;
- required repository paths and expected changed paths;
- validation commands;
- manual application test policy;
- exact staging paths;
- signed commit header, body, footer, and execution mode;
- post-commit validation commands.

Use `scripts/patch/templates/manifest.json` as the starting point.

## Build a package

Prepare a directory whose name matches `patch.name`. Include `manifest.json`,
`apply.sh`, and every payload file declared by `package.files`. Checksum fields
may initially be empty.

```bash
make patch-pack \
  PATCH_DIR=/path/to/alicia-live-result-preview-01 \
  PATCH_OUTPUT=$HOME/Téléchargements/alicia-live-result-preview-01.zip
```

The packer computes manifest checksums, creates `SHA256SUMS`, validates the
complete directory, and produces a ZIP with exactly one top-level directory.

Validate without packing:

```bash
make patch-validate PATCH_DIR=/path/to/package
```

Run workflow tests:

```bash
make patch-self-test
```

## Interactive controls

- `PATCH_ASSUME_YES=1`: answer yes to prompts;
- `PATCH_NON_INTERACTIVE=1`: reject prompts that require user input;
- `NO_COLOR=1`: disable console colors;
- `PATCH_DOWNLOADS_DIR=/path`: override `~/Téléchargements`.

A required manual test always blocks the commit when it is skipped or rejected. Automated and manual validation run before staging, so rejected or interrupted manual checks preserve authorized changes as unstaged worktree modifications.

Interactive application commands run in a dedicated process session. The runner remains available for the acceptance prompt and stops the complete child process group after acceptance, rejection, or workflow interruption. Application output is stored in the manual-test command log; stopping the application with `Ctrl+C` is not required.
