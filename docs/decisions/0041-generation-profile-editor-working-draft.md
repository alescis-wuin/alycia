# ADR 0041 - Generation-profile editor and explicit WorkingDraft persistence

## Status

Accepted.

## Context

Lot 10B.3 already provides model-scoped immutable generation-profile revisions, one optional `GenerationProfileWorkingDraft` per custom profile, stale-draft detection through the draft base revision, and atomic JSON persistence through `IGenerationProfileCatalogStore`.

UIX-02 Stage 1 exposes confirmed profiles in Models and binds one stable `GenerationProfileId` to the active conversation branch. It deliberately does not mutate profiles, so a normal user still has no Presentation path to create a custom profile or revise an existing one.

The complete UI/UX specification ultimately requires automatic local draft persistence, a pre-send choice between the previous and modified profile, and revision-history visualization. Combining those policies with the first editor would make one UI patch own persistence, generation gating, and history at once. This stage therefore exposes the existing revision/draft contracts first and keeps autosave and pre-send policy separate.

## Decision

### Dedicated Presentation editor

A dedicated `GenerationProfileEditorViewModel` owns editable profile fields instead of adding them to the `MainViewModel` facade:

- name;
- optional base system instructions;
- optional maximum output tokens;
- optional temperature, top-p, top-k, and seed;
- reasoning mode: provider default, explicitly disabled, or enabled;
- explicit reasoning budget when reasoning is enabled;
- initial suggestions, represented in the editor as one suggestion per line.

Blank numeric fields preserve provider/model defaults. The reasoning mode keeps the distinction between `null`, `false`, and `true`; no provider-default value is silently converted to an explicit disable.

The built-in `Default` profile remains read-only and never creates a working draft or user revision.

### New custom profiles

`New profile` opens an editor with a new stable `GenerationProfileId` but does not immediately mutate the persisted catalog.

`Save draft locally` persists a `GenerationProfileWorkingDraft` with `BaseRevisionId = null`. A draft-only new profile therefore remains absent from `GenerationProfileCatalog.Profiles`, which continues to project confirmed profiles only.

`Save revision` writes the draft and commits it in one catalog update, creating the first immutable `GenerationProfileRevision`. The newly confirmed profile then appears in the model-scoped profile list and becomes the selected profile in Models. Binding that profile to a conversation branch remains a separate explicit action.

### Existing custom profiles

`Edit profile` opens only a confirmed custom profile. Presentation restores an existing WorkingDraft when present; otherwise it loads the latest confirmed revision.

Saving a draft keeps its exact `BaseRevisionId`. Committing produces a new revision through `GenerationProfileCatalog.CommitWorkingDraft`, so the existing linear-chain and stale-base invariants remain authoritative in Application.

A persisted stale draft is shown rather than silently rebased. Stage 2 disables commit while stale and leaves `Discard draft` available. Discard removes the WorkingDraft and restores the latest confirmed revision.

### Persistence boundary

Presentation continues to depend only on `IGenerationProfileCatalogStore`; Desktop injects the JSON Infrastructure adapter. The editor does not depend on filesystem paths or JSON contracts.

This stage uses explicit `Save draft locally` rather than automatic persistence on every field edit. That is intentional and temporary. A later UIX-02 stage may add debounced autosave once cancellation/error semantics and the modified-profile pre-send gate are implemented together.

### Generation interaction

Profile mutation is disabled while a generation is active or while another `MainViewModel` operation owns the busy state. This avoids committing a profile revision concurrently with an active response from the same UI surface.

A branch selection stores the stable `GenerationProfileId`, not a revision. As defined by Lot 10B.4, the generation resolver continues to resolve the latest confirmed revision at the beginning of a future turn and captures that exact revision in `GenerationSnapshot`.

This stage does not change `GenerationSnapshot`, branch persistence, provider adapters, or conversation context.

## Consequences

- custom profiles can now be created and revised through Models;
- a new profile can exist durably as a WorkingDraft without appearing as a confirmed profile;
- existing WorkingDrafts survive restart and are restored when editing the profile;
- confirmed saves create immutable revision-chain entries through the existing Application invariant;
- stale drafts are preserved and cannot be committed silently;
- discarding a draft restores the latest confirmed profile state;
- provider/model defaults remain representable as `null` generation options;
- the Default profile remains immutable;
- branch binding remains explicit and independent from editing a profile;
- automatic WorkingDraft save, the pre-send previous/modified choice, profile revision-history UI, profile deletion, full model-library dual selection, branch navigation, context UI, RAG, tools, and multimodal remain outside this stage.
