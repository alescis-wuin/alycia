# ADR 0044 - Persistent model-library foundation

## Status

Accepted.

## Context

The UI/UX specification requires a model/profile dual selector and a persistent Models library. The current provider-configuration contract stores only one saved configuration per provider, while conversation generation selections already identify an explicit provider/model scope. `ConversationGenerationResolver` intentionally refuses to generate when the branch-selected model differs from the currently saved provider model.

Building a multi-model selector directly in Presentation would therefore imply a library capability that does not exist in Application/Infrastructure and would violate the roadmap rule that UIX must wait for real business contracts instead of simulating them in Presentation.

A library entry also cannot mean "loaded": Alicia currently supports one loaded model at a time and provider start/load remains an explicit operation.

## Decision

### Provider-neutral saved model entries

Application adds `InferenceModelLibraryEntry` and `InferenceModelLibrary`.

Each entry stores an immutable snapshot of an existing `InferenceProviderConfiguration` that has a non-empty model reference, plus the UTC instant at which that snapshot was saved to the library. The snapshot therefore includes:

- provider identifier;
- model reference;
- optional context size;
- provider-neutral generation defaults/overrides already represented by `InferenceGenerationOptions`.

The model identity inside the library is the exact `(ProviderId, ModelReference)` pair. Saving the same pair again replaces its saved configuration instead of creating a duplicate.

### Explicit persistence

`IInferenceModelLibraryStore` is a separate Application port. Desktop composes `JsonInferenceModelLibraryStore`, stored at `providers/models.json` with schema version 1 and atomic replacement.

An absent file remains an absent/empty library projection. Existing provider configuration is not silently migrated into the library. The user explicitly chooses `Save current model to library`.

No secret, API key, endpoint, prompt, response, reasoning content, or provider diagnostic is stored in this document.

### Presentation behavior

Models exposes the saved library as a read-only list of model cards with provider/model identity, quantization when encoded in the model reference, runtime context summary, saved timestamp, and whether the entry matches the currently saved provider model.

`Save current model to library` is enabled only for a valid configuration that has already been saved through the existing provider-configuration workflow. It does not start or reload the provider.

`Use selected settings` copies the selected library entry into the existing editable provider/model settings. It does not persist those settings, select a conversation profile, start the provider, or load a model. The existing `Save settings` action remains the explicit persistence boundary.

A library entry owned by another provider cannot be copied into the current provider draft implicitly.

### Boundaries

This stage does not change:

- `IInferenceProviderConfigurationStore` semantics;
- `ConversationGenerationSelection` or branch binding;
- `ConversationGenerationResolver`;
- provider start/load lifecycle;
- generation-profile catalogs or revisions;
- `GenerationSnapshot` provenance.

Model-library deletion, local cache deletion, automatic model switching/loading, the composer model/profile dual selector, visual model/profile dual-box navigation, and model-selection history remain later stages.

## Consequences

- multiple model configurations can be retained without overwriting the one active provider configuration;
- reusing a model configuration is explicit and non-destructive;
- the library cannot falsely imply that a saved model is currently loaded;
- existing branch/model/profile generation invariants remain unchanged;
- Stage 6 can build the full dual selector against a real persistent multi-model source rather than a Presentation-only list.
