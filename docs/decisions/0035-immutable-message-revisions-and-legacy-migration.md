# ADR 0035 - Immutable message revisions and deterministic legacy migration

## Status

Accepted.

## Context

Lot 10B.4 binds each started turn to an immutable `GenerationSnapshot`, but the conversation aggregate still persists a linear list of `ChatMessage` values identified only by `MessageId`. The UI/UX revision contract requires a stable logical entity identifier plus a distinct immutable revision identifier, parent link, timestamp, self-contained payload, and deterministic payload hash before generation provenance or branching can reference exact message states.

The existing conversation JSON schema v2 and schema-less legacy documents contain no revision identifiers. Generating random revision identifiers every time one of those documents is read would make stale-history checks and future provenance unstable before an explicit save occurs.

This step must establish message-revision identity without prematurely introducing the generation-snapshot registry, conversation graph, context revisions, or branch UI.

## Decision

### Message revision identity

Introduce Domain-level `MessageRevisionId` as a typed UUID. `MessageId` remains the stable identifier of the logical message entity.

`ChatMessage` remains the immutable message value consumed by the existing linear conversation APIs, but now also represents one confirmed revision and carries:

- `MessageRevisionId RevisionId`;
- optional `MessageRevisionId ParentRevisionId`;
- role;
- content;
- revision timestamp through the existing `CreatedAt` property;
- deterministic SHA-256 `PayloadHash`.

The payload hash uses the tagged canonical representation `alicia-message-revision-payload-v1` and includes the stable `MessageId`, role, content, and timestamp. Revision identity and parent metadata are not part of the payload hash, so two revision records with identical self-contained payloads correlate to the same payload hash.

A revision identifier cannot be empty, a supplied parent identifier cannot be empty, and a revision cannot parent itself. The current linear `Conversation` still accepts at most one active value for a given `MessageId`, and now also rejects duplicate `MessageRevisionId` values.

The existing four-argument `ChatMessage` constructor remains as a source-compatible convenience for creating a new root revision. Application persistence paths create the root revision identifier explicitly when a new user or finalized Assistant message is produced. A partial stream still creates no historical Assistant revision.

### Conversation JSON schema v3

`JsonConversationRepository` writes schema v3. Each persisted message now includes its revision identifier, optional parent revision identifier, and payload hash alongside the existing message fields.

Schema v3 reads reconstruct `ChatMessage` and recompute the payload hash. Missing or mismatched hashes are rejected instead of normalized silently.

Schema-less documents and schema v2 remain readable. Their single historical state for each message is mapped to a root revision with `ParentRevisionId = null` and a deterministic UUID derived from:

- a fixed migration-domain tag `alicia-legacy-message-revision-id-v1`;
- `ConversationId`;
- `MessageId`.

The derived identifier uses SHA-256 material encoded as an RFC-variant UUID with version nibble 8. It is therefore stable across repeated reads before any save. Saving a legacy aggregate materializes the same identifier in schema v3 rather than replacing it with a random revision UUID.

### Scope boundary

Lot 10B.5a does not add editing, historical revision registries, `GenerationSnapshotId`, durable Assistant provenance, branches, context revisions, context-budget accounting, RAG, or Presentation branch/timeline surfaces.

Those remain the next atomic boundaries:

- 10B.5b: durable generation-snapshot/provenance registry and Assistant revision binding;
- 10B.5c: conversation branch graph and edit/branch semantics;
- 10B.6: context revisions/provenance and explicit context budget.

## Consequences

- Every newly persisted message state has explicit immutable revision identity before provenance is attached to it.
- Legacy v0/v2 conversations can participate in exact-revision correlation without unstable identifiers across reads.
- Conversation JSON gains integrity checking for message revision payloads while remaining backward-readable.
- Current history/search/preview consumers can continue using the linear `Conversation.Messages` projection during this atomic step.
- The Domain remains independent from Application; no `GenerationSnapshot` type is introduced into Domain.
- Future 10B.5b/10B.5c work can reference `MessageRevisionId` directly rather than retrofitting revision identity after provenance or branching exists.
