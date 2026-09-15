# ADR 0020 — Presentation ViewModel decomposition

## Status

Accepted — UIX-01 Stage 9.

## Context

`MainViewModel` accumulated conversation selection and lifecycle state, history/search state, streaming projection, provider lifecycle state, model configuration, generation settings, and cross-cutting command orchestration. The behavior was covered by the Presentation test suite, but the single class had become the primary structural bottleneck for the remaining UI foundation work and for future provenance/RAG/tool features.

UIX-01 Stage 9 must reduce that coupling without changing Domain/Application contracts or introducing a broad UI rewrite before Stage 10.

## Decision

Presentation now composes six explicit slices:

- `ConversationWorkspaceViewModel` owns top-level conversation workspace state such as selection, draft, busy/error and destructive-action visibility;
- `ConversationHistoryViewModel` owns the history collection, search projection state and history-panel projection state;
- `ConversationStreamViewModel` owns the message projection plus sending/generation and Reduced Motion presentation state;
- `ProviderViewModel` owns provider selection/runtime projection, lifecycle snapshot and structured operation progress;
- `ModelViewModel` owns persisted provider/model configuration state, model/context draft state and configuration dirty/validation semantics;
- `GenerationSettingsViewModel` owns provider-neutral sampling/reasoning draft values and parsing into `InferenceGenerationOptions`.

`MainViewModel` remains the orchestration boundary for this stage. It coordinates Application use cases and cross-slice commands, and retains forwarding properties matching the existing binding/test contract. This compatibility facade is intentional: Stage 9 changes ownership and dependency direction first, while Stage 10 can evolve visual composition without coupling that work to a second behavioral rewrite.

No Domain, Application, Infrastructure, persistence, provider protocol, or conversation document contract changes are introduced by this decision.

## Consequences

### Positive

- state ownership is explicit instead of inferred from one monolithic class;
- model/generation validation is isolated from provider runtime projection;
- conversation collection/message projection ownership is visible and testable;
- future views can bind directly to specialized slices incrementally;
- `MainViewModel` becomes an orchestration/compatibility facade rather than the long-term home for every Presentation concern;
- the existing behavioral suite remains the regression contract for the refactor.

### Trade-offs

- Stage 9 intentionally retains forwarding properties and some cross-slice orchestration in `MainViewModel`, so the class is not eliminated;
- child ViewModels are not independent application services and must remain Presentation-only;
- direct child binding is optional until the Stage 10 responsive/gate pass, avoiding a simultaneous XAML and behavior migration.

## Validation

The normal repository quality gate must remain green. Presentation tests additionally verify that the public facade and specialized slices share the same history/message collections and that draft/generation state is owned by the corresponding child ViewModels.
