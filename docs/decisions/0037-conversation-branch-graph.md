# ADR 0037 - Immutable-prefix conversation branch graph

## Status

Accepted.

## Context

Lot 10B.5a gives each confirmed message state a stable logical `MessageId` and an immutable `MessageRevisionId`. Lot 10B.5b durably binds finalized Assistant revisions to the exact generation snapshot and ordered input revision identifiers that produced them. The conversation aggregate, however, still exposes only one destructive linear history.

Editing an older User message must not rewrite or delete already confirmed message revisions, Assistant provenance, or downstream history. A revised path must reuse the unchanged prefix, replace only the edited logical message with a new revision, and preserve the original continuation as a selectable branch.

This step must introduce durable branch semantics without adding context revisions/budgeting, RAG, tools, multimodal payloads, or a Presentation timeline/branch navigator.

## Decision

### Stable branch identity and immutable shared prefixes

Introduce Domain-level `ConversationBranchId` as a typed UUID and immutable `ConversationBranch` metadata containing:

- branch identity;
- optional parent branch identity;
- optional `ForkedAfterRevisionId`, identifying the last revision shared with the parent;
- an ordered defensive list of branch-local `MessageRevisionId` values.

A conversation stores every immutable `ChatMessage` revision exactly once in a global revision registry. Each revision is owned by exactly one branch-local tail. A child branch resolves its visible history as its parent history truncated at `ForkedAfterRevisionId`, followed by its own local revisions. A null fork revision on a non-root branch means the child shares no message prefix with its parent.

This structure shares confirmed prefix revisions by identity instead of copying message payloads. Parent branches may later receive additional messages without altering an existing child, because child resolution is permanently truncated at its recorded fork revision.

### Active branch compatibility view

`Conversation.Messages` remains the source-compatible ordered history surface used by existing Application and Presentation code, but now represents only the active branch. The aggregate additionally exposes the complete immutable revision registry, branch metadata, active branch identity, and read-only branch history lookup.

New conversations start with one empty root branch. Activating an existing branch changes only the persisted active-branch selection; it does not rewrite message revisions or advance conversation activity metadata.

### Editing creates a child branch

Editing is initially restricted to a User revision visible on the active branch.

An edit:

1. keeps the stable logical `MessageId`;
2. creates a fresh `MessageRevisionId`;
3. links `ParentRevisionId` to the exact edited revision;
4. preserves the User role and stores the new self-contained payload/timestamp;
5. creates a child branch of the currently active branch;
6. sets the fork point to the message immediately before the edited revision, or null when editing the first message;
7. makes the new revision the first local revision of the child branch;
8. activates the child branch.

All descendants after the edited revision remain untouched on the original branch. The edited child therefore contains the immutable shared prefix plus the new revision and can receive new Assistant/User revisions independently.

The aggregate rejects duplicate revision ownership, orphan revisions, unknown parents/fork points, multiple root branches, branch cycles implied by out-of-order/unknown parents, and any resolved branch path that contains two revisions of the same logical `MessageId`.

### Application orchestration and stale-turn protection

Application adds explicit edit and branch-activation use cases. Existing append and generation use cases continue to operate against `Conversation.Messages`, so they naturally target only the active branch.

Turn stale-history validation is strengthened to snapshot the complete revision registry, branch graph, and active branch identity. A branch switch or branch-graph mutation while generation is in flight therefore invalidates completion even when other conversation metadata has not changed.

Detached turn completion clones the complete graph before appending the finalized Assistant revision, preserving rollback behavior introduced by Lot 10B.5b.

### Persistence migration

`JsonConversationRepository` advances to schema v5 with separate `messageRevisions`, `branches`, and `activeBranchId` fields.

Schemas 0, 2, 3, and 4 remain readable. Their former linear message list is projected onto one deterministic root `ConversationBranchId` derived from the conversation identifier. Repeated legacy reads therefore produce the same branch identity before any explicit save. The next save materializes schema v5.

Message payload-hash and generation-snapshot-reference rules remain unchanged. Branch metadata is relational structure and does not alter the existing message payload-hash contract.

## Consequences

- Editing an older User message is non-destructive: the original branch and every downstream revision/provenance reference remain intact.
- Unchanged prefixes are shared by immutable revision identity rather than copied.
- Existing response generation continues to consume a linear ordered message list, now scoped to the active branch.
- Generation snapshots after a fork naturally capture only the exact active-branch revision sequence sent to the provider.
- Legacy conversation files gain a stable deterministic root branch when read and upgrade to schema v5 on save.
- Branch selection and editing are available at Domain/Application boundaries; a visual branch timeline/editor remains a later Presentation concern.
- Context revisions, context provenance and explicit context-budget accounting remain Lot 10B.6.
