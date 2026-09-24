# ADR 0049 — Two-line composer and branch model/profile readiness gate

## Status

Accepted — UIX-03 Stage 4A.

## Context

UIX-02 Stage 6 introduced a real branch-scoped model/profile selector backed by the persistent model library, model-scoped profile catalogs, and branch-aware generation selection. UIX-03 Stages 1–3 then separated settings ownership, moved profile editing into a dedicated workspace, and clarified Models as saved-model navigation plus loading settings.

The Conversation composer already exposes the model/profile selector, branch readiness text, Send/Stop/Retry actions, and the global configuration gate. The current markup, however, places the message field on one row and both the selector and response actions on the second row. UIX-03 Stage 4 targets a clearer two-line hierarchy without changing the persistence or provider lifecycle contracts behind those controls.

Two different readiness concepts must remain distinct:

1. global local-AI availability, owned by the existing Conversation configuration gate from ADR 0022;
2. branch model/profile readiness, owned by the existing dual-selector projection and pre-send guards from ADR 0045.

Stage 4 must improve composition hierarchy without collapsing those concepts into one implicit model-loading action.

## Decision

### Two semantic composer lines

When the Conversation configuration gate is hidden and the composer is available, the composer is organized into two semantic lines:

1. **message / response actions** — the multiline message field plus Send and the existing Stop/Retry response actions;
2. **model / profile readiness** — the compact branch model/profile selector plus concise readiness/status guidance.

The implementation may wrap content at the minimum supported viewport, but it must preserve this semantic order and must not make controls horizontally inaccessible.

The message field remains the primary composer surface. Enter sends when the existing send command is enabled; Shift+Enter inserts a newline. Stop and Retry keep their existing command semantics and transient-state guards.

### Global configuration gate remains authoritative

ADR 0022 remains unchanged.

If provider/model availability causes `ConversationConfigurationGateViewModel` to become visible, the configuration gate still replaces the composer. Stage 4 does not duplicate Detect/Install/Load/Provider/Models recovery actions inside the compact composer.

The Stage 4 branch readiness surface therefore applies only when the normal composer is otherwise available.

### Branch model/profile readiness stays inside the composer

The existing branch-scoped selection remains explicit and non-destructive.

When the active branch expects a model that does not match the currently saved provider/model configuration, or when a different model is actually loaded:

- the composer remains visible;
- the model/profile selector remains accessible;
- Send remains disabled through the existing readiness guard;
- concise readiness guidance explains which model the branch expects and directs the user to Models when configuration/loading work is required;
- no provider/model mutation occurs implicitly.

`GenerationDualSelectorReadinessText` remains the current source of branch-readiness guidance unless a Presentation-only projection is introduced solely to format the same semantics more compactly.

### Selector persistence boundary is unchanged

Opening the selector, moving selection, or inspecting another model/profile pair never persists or loads anything.

`Use for this branch` remains the only dual-selector persistence boundary and keeps the ADR 0045 semantics:

- persist the selected branch `ConversationGenerationSelection`;
- persist an empty Default catalog only when required by the existing selector contract;
- never save provider configuration;
- never start, stop, switch, load, or unload a provider/model;
- never copy model-library settings into Models implicitly;
- never commit or replace a profile WorkingDraft.

Stage 5 may replace the selector's current modal-like surface with a drawer/panel, but it must preserve this persistence boundary.

### Profile WorkingDraft gate stays separate

The existing pre-send profile WorkingDraft gate remains a separate transient surface. Stage 4 does not merge the `Use previous version` / `Use modified version` decision into branch model readiness.

A send attempt therefore continues to respect both independent guards:

- branch model/profile/runtime readiness before generation;
- confirmed-versus-WorkingDraft profile choice when the resolved profile has a local draft.

### Accessibility and responsive requirements

Stage 4 implementation must retain or improve:

- accessible names/help text for message, Send/Stop/Retry, and model/profile selector controls;
- polite live announcement of branch readiness text when it changes;
- visible keyboard focus;
- logical keyboard order matching the two semantic composer lines;
- Enter/Shift+Enter behavior;
- existing Escape precedence for transient surfaces;
- no state communicated by color alone.

The implementation must remain usable at the desktop minimum viewport already covered by the headless suite. Exact final spacing, truncation, and visual polish remain part of UIX-03 Stage 7.

## Required focused tests

Stage 4 implementation must retain or add tests proving that:

- the ADR 0022 configuration gate still replaces the composer when global provider/model readiness is unavailable;
- an otherwise available composer remains visible when only the branch model/profile selection is mismatched;
- branch mismatch keeps the selector available while Send is disabled;
- matching saved and loaded branch-model state restores the existing send readiness path without a new persistence side effect;
- opening/changing/cancelling the selector does not save provider configuration, start/stop a provider, load a model, or change branch selection;
- `Use for this branch` remains the explicit persistence boundary;
- Enter sends and Shift+Enter inserts a newline exactly as before;
- Stop/Retry visibility and guards are unchanged;
- the two semantic composer lines remain horizontally contained and keyboard-reachable at the deterministic headless viewport matrix relevant to Conversation.

## Boundaries

Stage 4 does not introduce:

- automatic provider switching;
- automatic model loading/reloading;
- provider lifecycle orchestration from the model/profile selector;
- new Domain, Application, Infrastructure, or persistence contracts;
- a new model-library schema;
- model/profile deletion;
- profile revision diffing;
- replacement of the dual selector with the Stage 5 drawer/panel;
- Provider workspace decluttering from Stage 6;
- final application-wide responsive/accessibility polish from Stage 7;
- branch navigation, context-panel UI, RAG, tools/agents, attachments, or multimodal behavior.

## Consequences

- the composer hierarchy matches the UIX-03 target without weakening existing execution boundaries;
- message composition and response actions become the primary line;
- model/profile intent and readiness become a compact secondary line;
- global provider readiness, branch selection readiness, and profile WorkingDraft readiness remain three explicit concepts;
- Stage 4B can remain primarily Presentation/XAML work with focused regression tests rather than a new backend capability.
