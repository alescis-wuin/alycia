# ADR 0013 — Global navigation rail and transitional workspace surfaces

## Status

Accepted.

## Context

ADR 0012 introduced `ShellViewModel`, `WorkspaceSection`, and `ShellView` while deliberately leaving the existing combined `MainView` unchanged. The UI/UX specification now requires the global scope to be represented spatially by a persistent icon-only rail at the far left and requires Conversations, Providers, and Models to become visibly distinct destinations.

Performing the full `MainViewModel` decomposition in the same change would couple global navigation behavior to conversation-history, provider-runtime, model-configuration, and streaming refactors. The existing shared state must therefore remain the migration boundary for this step.

## Decision

- Add a persistent 56 px icon-only global navigation rail for Conversations, Providers, and Models.
- Keep navigation labels out of the layout by showing them in an overlay flyout to the right of the rail.
- Open the flyout after approximately 275 ms of deliberate pointer hover, keep it open while the pointer remains on the rail/flyout, and close it approximately 450 ms after leaving both surfaces.
- Open the flyout immediately when navigation receives keyboard focus; `Escape` closes it while focus is in the navigation surface.
- Preserve one selected-state marker and accessible names/tooltips for every icon-only destination.
- Route `ShellViewModel.SelectedSection` to three visual surfaces without constructing another `MainViewModel`.
- Keep `MainView` as the Conversation surface for now.
- Add transitional Provider and Models surfaces bound to the same shared `MainViewModel`, exposing only capabilities that already exist in the current presentation contract.
- Do not change provider runtime contracts, conversation persistence, streaming behavior, or model lifecycle semantics in this step.

## Consequences

- Global navigation is now visible and functional without duplicating provider/runtime or conversation state.
- Provider lifecycle and model configuration can be reached from dedicated destinations before their ViewModels are extracted.
- The current `MainView` still contains the legacy combined provider/model panel, so those controls are temporarily duplicated across visual surfaces. Removing that legacy panel is the next migration step.
- The flyout is transient view state owned by `ShellView`; it is intentionally not persisted in `ShellViewModel`.
- The next UIX step can refactor `MainView` into a Conversation-only surface while retaining the established shell and navigation contract.
