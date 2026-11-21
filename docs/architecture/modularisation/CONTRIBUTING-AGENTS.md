# Independent changes to Alycia

Start from the exact reviewed integration commit. The phase sequence for this refactor is available with `git log --first-parent --reverse --oneline 309d610..HEAD`. The [ownership map](ownership.md) identifies the table, file and transaction owners. The [reference inventory](baseline.md) lists the public routes.

## Task contract

Copy the table into the task or pull request before starting.

| Field | Fill in |
|---|---|
| Outcome and scope | One behavior, acceptance condition and excluded work |
| Base | Exact commit returned by `git rev-parse HEAD` and target branch |
| Owner | Domain, named application port, data/transaction owner |
| Allowed files | Concrete feature directory, focused test files and fixtures |
| Shared files reserved for integration | Solution, project references, DI, shared Contracts/ChatOptions, shared route registration, migration history, global scripts |
| Frozen interfaces | Method, DTO, versions, JSON/NDJSON, affected public status codes |
| Invariants | Cancellation, owner isolation, revision/replay, privacy, resource/transaction boundaries relevant to this change |
| Evidence and delivery | Commands, provider/DB mode, counts, skipped cases, commit SHA and residual risks |

## Working independently

1. Verify the base is present, then use `git worktree add -b feature/<area>-<change> ../alycia-<area> <base-sha>`. Assign disjoint files. Split dependent work only after its interface has landed.
2. Change the owning feature and its tests. A new use case normally fits a feature directory in Domain, Application or Infrastructure. Put an interface beside its consumer if a second implementation or an external boundary calls for it. Keep query adapters, provider I/O and EF in Infrastructure. Put user-visible shared contracts in Alycia.Contracts and map them explicitly at the transport boundary.
3. To add an assembly, document the independent consumer/test boundary, choose the dependency direction, update the solution and Docker restore input through the integrator, then run `python3 scripts/check-modules.py`. A new persistent field needs an explicit compatibility decision and independently reviewed migration.
4. Run the affected project, for example `dotnet test tests/Alycia.Modules.Chat.Tests --filter FullyQualifiedName~StreamingTests`. Run `bash scripts/quality.sh lint` and `git diff --check`. Use a dedicated test PostgreSQL role with CREATEDB for persistence tests. Keep prompts, credentials, models and test databases out of commits. Report what was exercised.
5. Commit on the feature branch and give the integrator its SHA, paths, contract and evidence. If a shared contract must change, agree on a separate prerequisite with the integrator. Recreate a worktree on that integrated base or merge it normally. Reconcile conflicting behavior before combining the patches.

## Integration and recovery

The integrator registers dependencies in the API, reviews the diff against the frozen public contract, verifies the graph, and reruns the consumer checks. Run the final campaign from [the execution report](rapport-execution.md). Open a review PR into the agreed base.

Integrations with several independent chantiers have the preceding phase as first parent. To inspect a phase, run `git diff <phase>^1 <phase>`. To try an earlier phase without touching an existing checkout, use `git worktree add --detach ../alycia-checkpoint <sha>`. For a reviewed rollback on a separate recovery branch, revert a normal commit with `git revert <sha>`, or a phase merge with `git revert -m 1 <sha>`, newest dependent phase first. Verify the same compatibility checks before review. The refactor uses the existing storage schema.
