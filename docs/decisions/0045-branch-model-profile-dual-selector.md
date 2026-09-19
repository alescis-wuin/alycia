# ADR 0045 - Branch model/profile dual selector

## Status

Accepted.

## Context

UIX-02 Stage 5 established a real persistent multi-model library without changing the provider configuration that is currently saved or loaded. Earlier Lot 10B and UIX-02 stages already established model-scoped generation-profile catalogs and branch-aware `ConversationGenerationSelection` persistence.

The Conversation composer still lacks the compact model/profile selector required by the UI/UX specification. A selector cannot safely imply that choosing a library model also changes the active provider runtime: `ConversationGenerationResolver` intentionally requires the branch-selected model scope to match the saved provider configuration, and llama.cpp runtime guards independently require the actually loaded model to match the resolved request.

The selector must therefore expose branch intent without weakening those existing execution boundaries.

## Decision

### Dedicated Presentation projection

Presentation adds `GenerationDualSelectorViewModel` with dedicated model/profile item projections. It consumes only existing Application ports:

- `IInferenceModelLibraryStore` for saved model configurations;
- `IGenerationProfileCatalogStore` for confirmed profiles and WorkingDraft presence;
- `IConversationBranchGenerationSelectionStore` for the effective branch selection and legacy conversation fallback.

No new Domain, Application, Infrastructure, or persistence schema is introduced by this stage.

### Model list and compatibility projection

The model column is sourced from the persistent Stage 5 library. Two compatibility entries may also be projected without mutating the library:

- the currently saved provider/model configuration when it is not yet in the library;
- the model already bound to the active branch when it is not in the library.

These entries are labelled as compatibility entries. They preserve existing persisted state instead of silently replacing it, but they are never inserted into the model library by opening or applying the selector.

For each model scope, the selector loads its model-scoped generation-profile catalog. If no catalog exists, Presentation projects an empty catalog with the built-in `Default` profile. That catalog is persisted only if the user explicitly applies that model/profile pair to the branch.

### Branch-scoped apply boundary

Opening the selector restores the effective selection for the active conversation branch. A legacy conversation-scoped selection remains visible as a fallback. Applying the same pair writes an explicit branch-scoped selection and therefore pins the fallback to the current branch.

`Use for this branch` persists only:

- the required empty `Default` catalog when the selected model had no persisted catalog yet;
- `ConversationGenerationSelection(ConversationId, ConversationBranchId, ModelScope, ProfileId)`.

The selected `ProfileId` always refers to a confirmed profile. A WorkingDraft may be indicated in the popup, but it is not silently substituted for the confirmed profile; the Stage 3 pre-send gate remains responsible for choosing previous versus modified profile content.

### No implicit provider mutation

Selecting or applying a library model never:

- saves provider configuration;
- switches provider;
- starts or stops a provider;
- loads or unloads a model;
- copies library settings into Models;
- changes generation-profile revisions.

When the effective branch selection does not match the currently saved provider/model configuration, the Conversation composer remains available so the selector can still be opened, but Send is disabled with an explicit instruction to configure and load the branch model in Models.

When the provider is running with a different model than the effective branch selection, Send is likewise disabled before any User message is appended or provider call is attempted.

### Transient-surface lifecycle

The dual selector participates in the existing transient-surface rules:

- unavailable during generation, busy operations, profile editing, delete confirmation, or the profile pre-send gate;
- `Escape`/the existing transient-cancel command closes it without persistence;
- changing conversation reloads the effective branch projection;
- clearing the selected conversation clears the selector projection so state from the previous branch is never retained visually.

## Boundaries

This stage does not add:

- automatic provider/model loading or switching;
- model-library deletion or cache deletion;
- profile deletion or visual revision diffing;
- model/profile selection history beyond the existing branch-aware store;
- branch navigation UI;
- context-panel UI;
- RAG, tools, agents, or multimodal behavior.

## Consequences

- the composer now exposes one compact model/profile control backed by real persisted contracts;
- model/profile intent is explicit and branch-scoped;
- legacy conversation-scoped selections remain backward compatible and can be explicitly pinned;
- saved-library state, branch-selection state, and loaded-runtime state remain distinct instead of being conflated;
- a mismatched branch model is blocked before generation rather than failing only inside the resolver/runtime path;
- future model-load orchestration can build on this selector without changing its persistence semantics.
