# ADR 0042 - Generation-profile WorkingDraft autosave and pre-send revision gate

## Status

Accepted.

## Context

UIX-02 Stage 2 exposes explicit custom-profile editing on top of the model-scoped revision catalog from Lot 10B.3. It deliberately requires an explicit `Save draft locally` action and does not change generation behavior when a confirmed profile also has an unconfirmed WorkingDraft.

The UI/UX specification requires two additional guarantees before profile editing is safe in normal conversation flow:

- field edits must be persisted automatically as a local WorkingDraft, including while a response is streaming;
- before a new user message is sent with an active profile that has an unconfirmed WorkingDraft, the user must explicitly choose the previous confirmed revision or the modified draft.

A branch selection stores a stable `GenerationProfileId`, while generation resolves the latest confirmed revision at turn start. A WorkingDraft therefore must never affect a response implicitly.

This stage must preserve the existing Application revision invariants, must not append the user message before the choice is resolved, and must not add profile-history UI, profile deletion, branch navigation, context UI, RAG, tools, or multimodal payloads.

## Decision

### Debounced local autosave

`GenerationProfileEditorViewModel` tracks a monotonically increasing edit version and whether the currently displayed values contain unpersisted changes.

Presentation schedules a local WorkingDraft save after a short debounce when an editable field changes. Programmatic loads, commits, restores, and discard operations do not count as user edits.

The autosave path uses the existing `IGenerationProfileCatalogStore`; no filesystem or JSON dependency is introduced into Presentation.

A completed asynchronous save can mark only the edit version it actually persisted as saved. If the user changed another field while the store write was in flight, the newer editor values are preserved and another autosave is scheduled. An older persisted snapshot is never copied back over newer text.

Invalid intermediate form values remain visible and are not persisted until they become valid. Closing the editor attempts to flush a valid pending autosave first.

### Editing during an active response

Draft editing and WorkingDraft persistence remain available while a response is streaming. Confirming a new immutable profile revision remains disabled while the normal busy/generation guard is active.

The editor for a confirmed custom profile is restored across the conversation refresh that occurs after the user message is appended, so it can remain available during the stream.

An autosave performed during an active response does not affect that response. The response continues with the confirmed revision already resolved at turn start.

### Gate before a new send

Before appending a new user message, Presentation flushes any pending autosave and examines the generation selection actually resolved for the active `(ConversationId, ConversationBranchId)`. It does not use an unsaved ComboBox choice.

If the resolved profile has no WorkingDraft, sending follows the existing path unchanged.

If the resolved profile has a persisted WorkingDraft, the user message is not appended and no provider call starts until one of these explicit choices is made:

- `Use previous version`: keep the WorkingDraft untouched and send with the latest confirmed revision;
- `Use modified version`: commit the persisted WorkingDraft as a new immutable revision first, then send the message.

The pending message stays in the composer if the gate is cancelled or if the modified-draft commit fails.

Retrying an already-persisted unanswered user message is not treated as a new send by this stage and keeps the existing Retry path.

### Stale WorkingDrafts

If the WorkingDraft base revision is not the latest confirmed revision, `Use modified version` is disabled. `Use previous version` and Cancel remain available. The stale draft is never silently rebased or committed.

### Accessibility and modal behavior

The pre-send choice is a modal Presentation surface with keyboard focus cycling, initial focus on Cancel, `Escape` cancellation, and focus restoration when it closes. While visible, the message composer cannot send another message.

## Consequences

- valid generation-profile edits are autosaved locally without an explicit save action;
- WorkingDraft persistence remains independent from immutable revision confirmation;
- newer editor input cannot be overwritten by an older asynchronous autosave result;
- profile drafts can continue to change during an active response without mutating that response's captured revision;
- no new user message is persisted before the previous/modified profile choice is resolved;
- choosing the previous version preserves the WorkingDraft;
- choosing the modified version creates an immutable revision before generation starts;
- stale drafts cannot be confirmed through the pre-send gate;
- branch selection continues to store a stable `GenerationProfileId`;
- no GenerationSnapshot schema, Domain conversation schema, Infrastructure profile schema, RAG, tools, multimodal, revision-history UI, profile deletion, or dual-selector completion is introduced here.
