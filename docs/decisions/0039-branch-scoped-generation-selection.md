# ADR 0039 - Branch-scoped generation selection and fork-time pinning

## Status

Accepted.

## Context

Lot 10B.5c makes conversation messages branch-aware and Lot 10B.6 makes persistent conversation context branch-aware. The generation selection introduced in Lot 10B.4 still remains keyed only by `ConversationId`, so every branch currently resolves the same provider/model scope and generation profile.

The UI/UX model requires model/profile/context to diverge independently after a fork. The existing generation-selection contract, however, has no revision timeline or message-anchored bindings. This bridge therefore establishes branch-scoped persistence and resolution first, while keeping the historical conversation-scoped entry as a migration fallback.

It deliberately does not claim that Alycia can reconstruct which model/profile selection was active at an arbitrary historical message boundary. Exact historical divergence inheritance would require a separate revision/binding contract if the future branch UI needs to expose that guarantee.

This bridge must remain below Presentation and must not add branch navigation UI, profile editors, RAG, tools, multimodal payloads, or a new generation-snapshot schema.

## Decision

### Branch-scoped selection identity

`ConversationGenerationSelection` gains an optional `ConversationBranchId`.

- a non-null branch identifier represents the current selection for one branch;
- a null branch identifier is retained only as the backward-compatible conversation-scoped selection from the Lot 10B.4 contract;
- newly pinned branch state uses an explicit non-empty branch identifier.

The provider/model scope and stable `GenerationProfileId` remain unchanged. Profile revision resolution still happens at the start of each generation and the exact resolved revision is captured by `GenerationSnapshot`.

### Explicit branch-aware storage capability

The historical `IConversationGenerationSelectionStore` contract is left unchanged. A new `IConversationBranchGenerationSelectionStore` capability adds branch-aware load and delete operations.

Adapters that only implement the historical interface continue to resolve conversation-scoped selections and are never treated as if they supported branch isolation. `JsonConversationGenerationSelectionStore` implements the explicit branch-aware capability.

### Persistence compatibility

`JsonConversationGenerationSelectionStore` advances from schema v1 to schema v2.

Schema v1 remains readable. Its conversation-scoped entry is represented as a legacy fallback rather than being assigned to an invented branch. Schema v2 can store that fallback alongside independent branch-scoped entries.

Branch-aware lookup resolves:

1. an exact `(ConversationId, BranchId)` entry when present;
2. otherwise the legacy conversation-scoped entry when present;
3. otherwise no generation selection.

A branch-specific save replaces only that branch entry. Deleting one branch entry does not delete the legacy fallback or sibling branch selections. Deleting a conversation selection through the historical conversation-level delete removes every selection owned by that conversation.

### Fork-time pinning

`EditMessageUseCase` captures the selection that the active parent branch resolves when the fork is created. When editing an old User message creates a child branch, that selection is copied into an explicit child-branch selection before the edited conversation is persisted.

The edited conversation is created as a detached aggregate copy. If conversation persistence fails after the child selection was stored, the new child selection is deleted as compensation. The original loaded aggregate is therefore not mutated before persistence succeeds.

Because the child owns its copied selection, model/profile changes made on the parent after the fork cannot rewrite child behavior.

This is fork-time pinning, not historical selection reconstruction. If the parent selection changed after the historical message boundary but before the edit that creates the branch, this bridge cannot infer the older selection because Lot 10B.4 did not persist selection revisions or timeline bindings.

### Branch-aware generation resolution

A new `IConversationBranchContextGenerationResolver` extends the existing resolver contracts without removing the legacy interfaces. `ConversationTurnSnapshot` exposes the active branch identifier to the Application orchestration, and `ConversationGenerationResolution` uses the branch-aware contract when available.

`ConversationGenerationResolver` uses branch-aware selection lookup only when the injected store exposes `IConversationBranchGenerationSelectionStore`; otherwise it keeps the historical conversation-scoped behavior. Existing resolver/store callers therefore remain source-compatible without gaining fake branch semantics.

No branch identifier is added to `GenerationSnapshot` in this bridge. Existing message-revision/context provenance and the Assistant's branch ownership remain unchanged; snapshot schema evolution should occur only when a separate provenance requirement justifies it.

## Consequences

- model/profile selection can diverge independently between branches after branch state is pinned;
- newly created branches pin the effective parent selection observed at fork creation time;
- later parent selection changes do not rewrite child behavior;
- schema v1 selection files remain readable and upgrade only on an explicit save;
- historical conversation-scoped store/resolver implementations remain source-compatible;
- branch-aware behavior is exposed only by an explicit capability interface;
- generation continues to capture the exact provider/model/profile revision actually used for each response;
- exact historical selection-at-divergence reconstruction remains a separate design problem if required by the full branch UI;
- no Presentation, RAG, tools, multimodal, tokenizer, or snapshot-schema work is introduced by this bridge.
