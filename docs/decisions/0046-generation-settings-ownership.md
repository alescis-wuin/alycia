# ADR 0046 - Generation settings ownership

## Status

Accepted.

## Context

UIX-02 established model-scoped generation profiles, immutable profile revisions, a persistent model library, and a branch-scoped model/profile selector. The Models workspace still exposed two different categories of settings in one form: settings that affect how a model is loaded and settings that affect an individual generation.

That overlap makes the ownership model difficult to predict. A user can currently see temperature, sampling, output limits, seed, and reasoning controls beside model loading settings even though the same generation controls already exist in generation profiles. Keeping both editing surfaces would make it unclear which values should be changed for a branch and which values require a model reload.

The existing `InferenceProviderConfiguration` and Stage 5 model-library schema also contain generation options. Removing those fields or migrating persisted data is a separate compatibility concern and is not required to establish a clearer user-facing ownership boundary.

## Decision

### Model settings own model loading

The Models configuration surface is limited to settings that affect model loading or require the model to be reloaded. For the current llama.cpp integration this Stage 1 surface contains:

- the model reference / GGUF repository and optional quantization suffix;
- the configured context size.

Provider lifecycle remains in Provider. Future runtime settings may be added to Models only when they are genuinely model-load/reload concerns.

### Profiles own per-generation behavior

The generation-profile editor is the sole user-facing editor for behavior that can vary per generation:

- base system instructions;
- maximum output tokens;
- temperature;
- top-p;
- top-k;
- seed;
- reasoning mode and reasoning budget;
- initial suggestions.

Stage 1 does not yet create a dedicated Profiles workspace. The existing profile section remains temporarily inside Models so ownership can be corrected before the Stage 2 navigation/layout move.

### Preserve legacy provider generation configuration

No persistence schema is migrated in this stage. `InferenceProviderConfiguration.Generation` remains readable for backward compatibility and for conversations that still follow the historical provider-configuration fallback.

Saving model loading settings follows these rules:

- when a provider configuration already exists, its persisted generation options are preserved unchanged;
- when no provider configuration exists yet, the new configuration uses `InferenceGenerationOptions` with provider/model defaults;
- hidden legacy generation draft values do not participate in model-loading validation or change detection.

This prevents a Models save from silently rewriting generation behavior while legacy data is still supported.

### Model-library reuse is loading-settings-only

The Stage 5 library schema is retained unchanged for compatibility. Existing entries may still contain generation fields, but the Models reuse flow copies only the model reference and context size into the editable model draft. Generation fields from a library entry are not injected into the Models draft.

The library current-state indicator likewise treats generation-only differences as irrelevant to model-loading ownership. A future schema migration may remove obsolete generation payload from model-library persistence, but that is outside this stage.

## Boundaries

This stage does not add:

- a dedicated Profiles navigation destination;
- a Models two-column library redesign;
- composer layout changes;
- model/profile selector drawer changes;
- automatic model loading or switching;
- profile deletion or model deletion;
- persistence-schema migration;
- RAG, tools, agents, or multimodal behavior.

## Consequences

- Models has one predictable responsibility: model identity and restart-sensitive loading configuration;
- profiles have one predictable responsibility: per-generation behavior;
- saving model settings cannot accidentally replace existing legacy generation options;
- new model configurations no longer create an implicit explicit reasoning choice;
- model-library reuse cannot silently change hidden generation behavior;
- Stage 2 can move profile management into a dedicated workspace without another ownership migration.
