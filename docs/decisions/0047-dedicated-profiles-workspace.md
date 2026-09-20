# ADR 0047 - Dedicated Profiles workspace

## Status

Accepted.

## Context

UIX-03 Stage 1 established a clear ownership boundary: Models owns model identity and restart-sensitive loading settings, while generation profiles own instructions, output limits, sampling, seed, reasoning, suggestions, WorkingDrafts, and immutable revision history.

The profile management surface still lives inside Models, however. That placement keeps two different user goals in the same workspace and makes model configuration appear more complex than it is. It also couples profile browsing to the branch-selection-oriented ComboBox state even though editing a model-scoped profile does not require an active conversation.

Stage 2 must move the existing capabilities without changing their persistence, revision, branch-binding, or generation semantics.

## Decision

### Add Profiles as a first-level workspace

The global shell exposes four destinations:

- Conversations;
- Provider;
- Models;
- Profiles.

`WorkspaceSection`, `ShellViewModel`, the icon rail, and the delayed navigation flyout all project the new Profiles destination. The shell continues to share the existing `MainViewModel`; Stage 2 does not introduce another Application or Infrastructure dependency.

### Move profile management out of Models

`ModelWorkspaceView` no longer contains confirmed-profile selection, profile creation/editing, WorkingDraft actions, or revision history. It remains focused on the persistent model library plus model loading/reload configuration.

`ProfilesWorkspaceView` becomes the sole management surface for:

- browsing confirmed profiles for the saved model scope;
- explicitly binding the selected confirmed profile to the active conversation branch;
- creating and editing custom profiles;
- WorkingDraft autosave/save/discard behavior;
- confirming immutable revisions;
- browsing revision history and restoring an older revision as a new WorkingDraft.

The built-in `Default` profile remains read-only. Existing stale-draft and immutable-history invariants remain unchanged.

### Separate profile browsing from branch binding

A saved model scope is sufficient to browse and edit profiles. An active conversation is not required.

Branch binding remains a separate explicit action. `Use on this branch` is enabled only when the existing branch-aware selection contract has an active conversation and branch and the selected confirmed profile differs from the currently resolved selection. Selecting a profile in the Profiles workspace never persists a branch binding by itself.

During an active generation or another guarded busy operation, profile browsing that could change the current Presentation selection remains disabled. Opening an editor also locks the confirmed-profile list until the editor is closed, preserving the existing single-editor workflow.

### Preserve all existing contracts

Stage 2 does not change:

- `GenerationProfile`, catalog, WorkingDraft, or revision contracts;
- profile or branch-selection persistence schemas;
- generation resolution or `GenerationSnapshot`;
- provider/model loading semantics;
- model library persistence;
- the Conversation composer model/profile selector;
- automatic model switching/loading behavior.

## Accessibility and visual structure

Profiles uses the existing dark workspace visual language and native controls. The left column is the stable confirmed-profile list and branch-binding surface; the right column contains the selected-profile summary, editor, and revision history. Accessible names, heading levels, live status text, visible focus, and existing command enablement remain part of the interaction contract.

## Boundaries

Deferred to later UIX-03 stages:

- Models library navigation/detail redesign;
- the two-line Conversation composer simplification;
- replacement of the model/profile popup with a drawer;
- Provider decluttering;
- final responsive/accessibility visual QA;
- profile deletion and advanced visual revision comparison;
- branch/context navigation UI.

## Consequences

- model loading and generation behavior now have separate first-level destinations;
- Models becomes materially smaller without losing any profile capability;
- profiles can be inspected and edited without creating or selecting a conversation first;
- branch usage remains explicit and cannot be changed by merely browsing profiles;
- all Stage 1 compatibility and UIX-02 revision invariants remain intact.
