# ADR 0043 - Generation-profile revision history and restore-as-draft

## Status

Accepted.

## Context

Lot 10B.3 already stores immutable, model-scoped `GenerationProfileRevision` chains. UIX-02 Stages 2 and 3 add explicit editing, persistent WorkingDrafts, debounced autosave, stale-draft protection, and a pre-send previous/modified gate. The remaining profile-history requirement is Presentation access to those already persisted immutable revisions and a safe way to reuse an older payload.

Moving the catalog head backward or editing an existing revision would violate immutability and provenance. Creating a WorkingDraft whose base is the historical revision would also make that draft stale whenever a newer confirmed revision exists. Historical restore must therefore copy payload, not rewind history.

## Decision

### Read-only timeline

Models exposes the confirmed revisions of the selected custom profile newest-first. Each item shows its ordinal, current-head state, UTC creation time, profile name, and a shortened payload hash. The built-in `Default` profile has no user revision timeline.

### Restore as WorkingDraft

Restoring an older revision copies that revision's immutable profile payload into a new `GenerationProfileWorkingDraft` whose `BaseRevisionId` is the **current** confirmed head. The historical chain is left untouched.

The restored draft opens in the existing profile editor for review. `Save revision` then uses the existing catalog commit invariant and appends a new immutable revision whose parent is the former current head. For `R1 -> R2 -> R3`, restoring R1 and confirming it produces `R1 -> R2 -> R3 -> R4`, where R4 contains R1's payload.

### Existing WorkingDraft guard

Historical restore never overwrites a persisted WorkingDraft. If one exists for the selected profile, restore is disabled until the user either confirms or discards that draft through the existing editor workflow. This keeps autosaved local work non-destructive.

The current head cannot be restored because doing so would create a redundant draft with no historical change.

### Boundaries

The restore operation uses the existing `IGenerationProfileCatalogStore`; no JSON schema changes are required. Branch bindings continue to reference the stable `GenerationProfileId`, and past `GenerationSnapshot` provenance remains unchanged.

Profile deletion, visual diff/comparison, model-library dual selection, branch navigation, context UI, RAG, tools, and multimodal remain outside this stage.

## Consequences

- users can inspect confirmed profile history without a persistence migration;
- an old profile payload can be reused without rewriting or moving history;
- restored drafts are immediately non-stale because they are based on the current head;
- existing autosaved drafts cannot be silently destroyed by a history action;
- confirming a restored draft preserves a contiguous linear revision chain;
- future generations bound to the stable profile use the newly confirmed head only after explicit confirmation;
- past generation provenance remains anchored to the revision captured at turn start.
