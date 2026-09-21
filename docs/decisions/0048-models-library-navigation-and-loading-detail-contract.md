# ADR 0048 - Models library navigation and loading-detail interaction contract

## Status

Accepted.

## Context

UIX-03 Stage 1 made Models the owner of model identity and restart-sensitive loading settings. UIX-03 Stage 2 moved generation-profile management into a dedicated Profiles workspace. Models can therefore be simplified around one user goal: inspect saved model entries and explicitly prepare the loading configuration for the active provider.

The current Models workspace already exposes the required business capabilities, but presents them as three vertical sections: a model-library list, an editable model configuration form, and a provider summary. UIX-03 Stage 3 must redesign that surface as library navigation plus a loading-detail view without changing persistence or provider semantics.

The persistent model library is intentionally limited. An entry identifies the exact `(ProviderId, ModelReference)` pair and stores a saved configuration snapshot plus `SavedAtUtc`. Schema v1 may still contain legacy generation options, but Models ignores them. The library does not prove that a model is currently loaded, does not own provider selection, and does not provide aliases, tags, cache metadata, download state, or deletion contracts.

A navigation/detail redesign must therefore make the existing distinctions clearer instead of inventing richer model-management state in Presentation.

## Decision

### Stage 3A is a design contract only

Stage 3A changes documentation only. It does not modify C#, XAML, Application or Domain contracts, JSON schemas, provider lifecycle, generation resolution, branch bindings, or `GenerationSnapshot` behavior.

Implementation is deferred to the following Stage 3 sub-stages and must remain compatible with this contract.

### Models uses a navigation + detail information architecture

On a wide surface, Models is organized as a master/detail workspace:

```text
Models
+---------------------------+------------------------------------------+
| Model library             | Saved model detail                       |
|                           |                                          |
| saved model A             | identity / provider / quantization       |
| saved model B             | saved context / saved timestamp / state  |
| saved model C             |                                          |
|                           | [Use model settings]                     |
|                           |                                          |
|                           | Model loading settings                   |
|                           | model reference                          |
|                           | context size                             |
|                           | [Save model settings]                    |
|                           | [Save current model to library]          |
|                           |                                          |
|                           | Provider/runtime summary                 |
+---------------------------+------------------------------------------+
```

On narrower surfaces the same regions may stack vertically. The exact responsive breakpoint remains an implementation concern; the information order and interaction semantics do not change.

The left side is navigation. The right side is inspection and explicit loading-configuration work. Selection itself is never an action boundary.

### Library selection is inspection-only

Selecting a library item may only change the selected Presentation item and the details projected for that item.

Selection must not:

- copy settings into the editable model draft;
- save provider configuration;
- write or update the model library;
- start, stop, or reload a provider;
- switch the selected provider;
- load or unload a model;
- change a conversation, branch, model binding, or generation profile.

Keyboard navigation through the list has the same inspection-only semantics as pointer selection.

### Saved-model detail projects only known data

The selected-library detail may display data already represented by `InferenceModelLibraryEntry` and its current Presentation projection:

- display name derived from the model reference;
- full model reference;
- provider identifier;
- quantization when encoded in the model reference;
- saved context size or provider/model default;
- saved UTC timestamp;
- whether the entry matches the currently saved provider model;
- whether the matching saved provider configuration differs by loading settings currently owned by Models.

The detail must not fabricate or infer:

- a loaded-model state from library membership;
- local cache/download state;
- file size or disk usage;
- aliases, tags, favorites, notes, or icons not backed by a contract;
- an effective context value when the persisted value is unspecified;
- generation behavior from legacy generation fields stored in a schema-v1 library entry.

Existing state wording remains valid: `Saved in library`, `Current saved model`, and `Current model • library settings differ`.

### Loading configuration remains a separate editable draft

The editable model reference and context size continue to represent the current provider's loading-settings draft. They are not the selected library item.

The UI must keep four concepts visually and semantically distinct:

1. the selected library entry used for inspection;
2. the current provider configuration already persisted by `IInferenceProviderConfigurationStore`;
3. the editable loading-settings draft in Presentation;
4. the provider runtime state exposed by the existing provider runtime projection.

No state label may collapse those concepts into a generic `current`, `active`, or `loaded` model claim.

### Explicit action boundaries are preserved

`Use model settings` is the only library-to-draft action. It is available only when the selected entry belongs to the active provider and provider settings are editable. It copies only model reference and context size. It does not copy legacy generation options and does not persist, start, load, switch, or bind anything.

`Save model settings` remains the draft-to-provider-configuration persistence boundary. Existing legacy provider-level generation options are preserved exactly as required by ADR 0046.

`Save current model to library` remains the provider-configuration-to-library persistence boundary. It snapshots only a valid configuration that has already been saved for the active provider. It does not start or reload the provider.

Stage 3 does not introduce a new `Load model` action. Provider start/load semantics remain where they are currently owned until a separate contract changes them.

### Provider mismatch is explicit and non-destructive

When a selected library item belongs to another provider:

- its saved detail remains inspectable;
- `Use model settings` is disabled;
- the UI explains that the entry belongs to a different provider;
- no provider switch is offered as an implicit side effect;
- changing provider remains an explicit Provider-workspace operation.

### Required interaction states

The implementation must represent the following states without changing their business semantics:

| State | Required behavior |
| --- | --- |
| Library capability unavailable | Explain that the library is unavailable; no synthetic entries. |
| Empty library | Show an explicit empty state while keeping model loading settings usable. |
| Library populated, no selection | Prompt for selection; do not mutate the loading draft. |
| Selected entry, active provider matches | Show details; allow `Use model settings` when settings are editable. |
| Selected entry, active provider differs | Show details; disable reuse and explain the provider mismatch. |
| Selected entry is current saved model | Show the existing current-saved state without claiming it is loaded. |
| Selected current model has different saved context | Show the existing settings-differ state. |
| Loading draft has unsaved changes | Keep library selection independent; existing validation/save state remains authoritative. |
| Loading draft is invalid | Surface existing validation; library inspection remains read-only. |
| Provider running/busy or generation guarded | Preserve existing command/editability guards; selection must not become an implicit mutation path. |

### Accessibility and responsive behavior are part of the contract

Stage 3 implementation must preserve or improve the existing accessibility contract:

- `Models` remains the level-1 heading and main landmark;
- library and loading-detail regions expose explicit headings;
- the saved-model list keeps a meaningful accessible name and native keyboard navigation;
- action buttons keep explicit accessible names and help text where the effect is not obvious;
- status and validation text that can change asynchronously retain appropriate polite live-region behavior;
- focus remains visible;
- no state is communicated by color alone;
- wide master/detail layout may stack on narrow surfaces without changing action order or semantics.

Exact responsive thresholds and final visual polish remain part of UIX-03 Stage 7.

### Presentation implementation boundary

The following implementation stages may add Presentation-only projection state required to render the detail cleanly, for example selected-item detail text, provider-mismatch explanation, or empty-state visibility.

They should prefer extending the existing `InferenceModelLibraryItemViewModel`, `ModelViewModel`, and `MainViewModel` projection only as far as necessary. Stage 3 is not a mandate for a broad `MainViewModel` decomposition and must not introduce an Application or Infrastructure dependency solely for visual convenience.

### Required focused tests for implementation stages

The implementation that follows Stage 3A must retain or add focused tests proving that:

- selecting a library item causes no persistence, provider lifecycle, provider switch, branch binding, or loading-draft mutation;
- `Use model settings` copies only model reference and context size;
- an item owned by another provider cannot be applied to the current draft;
- saving model loading settings preserves existing legacy generation configuration;
- saving the current model to the library does not save provider configuration again or start/load a provider;
- generation-only differences do not mark library loading settings as different;
- selected model identity is restored by exact provider/model pair when the library projection refreshes;
- empty and unavailable library states remain explicit;
- existing busy/running editability guards remain authoritative.

## Boundaries

Stage 3 does not add or redesign:

- model-library deletion or local-cache deletion;
- aliases, tags, favorites, notes, images, or custom display names;
- search/filter contracts that require new persisted metadata;
- automatic provider switching;
- automatic model start/load/reload on selection;
- a new runtime loaded-model identity contract;
- model-library schema migration;
- generation-profile editing or profile persistence;
- composer model/profile selection;
- branch/context navigation;
- Provider workspace lifecycle behavior;
- RAG, tools/MCP, agents, attachments, or multimodal behavior;
- a broad Presentation architecture refactor.

Older UI/UX target sections that describe automatic creation, loading, or deletion of models are future targets only. They do not expand Stage 3 implementation scope unless backed by dedicated Application/Infrastructure contracts and a later ADR.

## Consequences

- Models gets a clearer information architecture without changing business semantics;
- users can inspect saved models without accidentally changing provider or conversation state;
- library state can no longer be visually confused with runtime loaded state;
- the loading draft has an explicit relationship to both saved provider configuration and a selected library entry;
- Stage 3 implementation can remain primarily Presentation work;
- later model-management features still require dedicated contracts instead of being simulated in the UI.
