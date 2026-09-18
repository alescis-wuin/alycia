# ADR 0040 - Active-branch generation-profile selection in Models

## Status

Accepted.

## Context

Lot 10B.3 provides immutable model-scoped generation-profile catalogs, Lot 10B.4 binds a model/profile selection to a conversation, and ADR 0039 adds an explicit branch-scoped selection capability. Presentation still exposes only the legacy provider/model configuration fields, so users cannot inspect the confirmed profiles already stored for the saved model or pin one to the active conversation branch.

The broader UI specification eventually calls for a Models/Profile dual selector, profile editing, revision history, model-library lifecycle, and branch navigation. Those capabilities do not yet share one complete Presentation contract. This stage must therefore expose the smallest useful surface backed by existing Application contracts without inventing a model library, silently creating confirmed profile state, or simulating branch history in Presentation.

## Decision

### Current saved model scope only

Models projects generation profiles only for the currently saved provider/model configuration. An unsaved provider/model draft disables branch-profile selection until the model configuration is saved.

This stage does not create a model library and does not load or unload models automatically. If a branch is pinned to another model scope, the UI reports that mismatch and requires an explicit profile save to rebind the branch to the currently saved model scope.

### Read/select first, edit later

Presentation lists confirmed `GenerationProfile` values from the model-scoped catalog and exposes one explicit action to use the selected profile for the active branch.

Profile creation, custom-profile editing, WorkingDraft editing, immutable revision commits, profile deletion, visual identity, and revision-history/restoration remain later UIX stages.

### Explicit Default materialization

The Application store deliberately treats a missing model scope as no catalog. Presentation therefore does not persist a catalog merely because Models was opened.

When no catalog exists, Models projects a transient `Default` profile so the user can make an explicit choice. Saving that choice first persists the corresponding empty catalog and then persists the branch selection. Merely viewing or selecting the workspace causes no generation-profile persistence.

### Active branch binding

`MainViewModel` retains the `ActiveBranchId` of the loaded conversation only as Presentation orchestration state until branch navigation receives its dedicated surface. The selected profile is saved through `IConversationBranchGenerationSelectionStore` with the active `ConversationId` and `ConversationBranchId`.

Branch-aware load semantics remain those from ADR 0039:

1. exact branch selection when present;
2. legacy conversation-scoped selection as fallback;
3. no selection otherwise.

A legacy fallback may be displayed as the effective profile, but the explicit Save action pins that same profile to the active branch instead of pretending the fallback is already branch-owned.

### Safe interaction boundaries

Profile selection is disabled when:

- no conversation/active branch is loaded;
- provider/model settings contain unsaved changes;
- the saved configuration has no model reference;
- a response generation or another busy Presentation operation is active;
- the host did not inject the required profile/branch-selection Application ports.

Changing the selected profile remains a draft until the explicit branch-save action succeeds.

### Composition

`Alicia.Desktop` injects the existing `IGenerationProfileCatalogStore` and `IConversationBranchGenerationSelectionStore` implementations into Presentation. Presentation depends only on Application contracts and does not reference Infrastructure implementations.

## Consequences

- users can inspect confirmed profiles for the saved model and explicitly bind one to the active branch;
- a missing catalog remains non-persistent until an explicit user action;
- legacy conversation-scoped selections can be migrated incrementally by explicit branch pinning;
- parent/child branch selection independence from ADR 0039 is now reachable from Presentation for the active branch;
- profile mutation and revision history remain isolated from selection semantics;
- full model/profile dual-box navigation still requires a real model-library/load-unload surface;
- branch navigation, context UI, provenance/timeline UI, RAG, tools, and multimodal remain outside this stage.
