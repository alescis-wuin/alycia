# ADR 0036 - Durable generation snapshot provenance and Assistant binding

## Status

Accepted.

## Context

Lot 10B.5a gives every confirmed conversation message state an immutable `MessageRevisionId`, but `GenerationSnapshot` is still an in-memory Application value without its own durable identity. Final Assistant revisions therefore cannot retain a stable reference to the generation state that produced them, and the snapshot does not yet identify the exact input message revisions sent to the provider.

The UI/UX provenance contract requires each generated Assistant revision to reference one immutable generation snapshot. That snapshot must identify the revisions that contributed to the request without copying prompt/message content, reasoning, response text, provider diagnostics, or future context records into a second persistence surface.

This step must make generation provenance durable without introducing the conversation branch graph, context revisions, explicit context-budget accounting, RAG, or provenance UI.

## Decision

### Stable snapshot identity and exact message inputs

Introduce Domain-level `GenerationSnapshotId` as a typed UUID so a Domain `ChatMessage` can hold an opaque provenance reference without depending on the Application-layer `GenerationSnapshot` type.

`GenerationSnapshot` remains an immutable Application contract and now carries:

- `GenerationSnapshotId`;
- `ConversationId`;
- triggering `MessageId`;
- triggering `MessageRevisionId`;
- the ordered, defensive list of every input `MessageRevisionId` sent to the provider;
- UTC capture time;
- requested provider/model/context and provider-neutral generation options;
- optional generation-profile identity and confirmed revision identity;
- deterministic SHA-256 `PayloadHash` over that self-contained provenance payload.

Snapshot identity is not part of the payload hash. Two separately identified snapshots with identical captured provenance therefore have the same payload hash and can support future deduplication without conflating identity with content.

The triggering revision must be the final captured input revision. Input revision identifiers must be non-empty and unique. `ConversationResponseRequest` verifies that a supplied snapshot's ordered input revisions match the request messages exactly and that its trigger matches the latest User message. This prevents a valid snapshot object from being attached to a different request payload.

### Profile-selected and legacy provider paths

A conversation with an explicit generation selection continues to resolve the latest confirmed custom-profile revision, or the revisionless built-in Default, exactly once at turn start.

A conversation without an explicit profile selection now also captures the persisted globally selected provider configuration when one exists. This does not synthesize a profile or profile revision: both profile fields remain `null`. It records and pins the same provider/model/context/options selected at turn start so the legacy path can produce truthful durable provenance instead of an unbound Assistant revision.

If no persisted provider selection exists, resolution remains absent and the existing provider error path is not replaced by invented configuration.

### Durable immutable snapshot registry

Application exposes `IGenerationSnapshotStore`. Infrastructure implements it with `JsonGenerationSnapshotStore`, using one schema-versioned JSON file per snapshot beneath the Desktop generation-data directory.

The store:

- derives the filename from `GenerationSnapshotId`;
- writes through a unique same-directory temporary file and atomic move;
- refuses to overwrite an existing snapshot identifier;
- reconstructs the Application value on read and recomputes `PayloadHash`;
- rejects malformed, identifier-mismatched, or hash-mismatched records;
- stores only revision identifiers and provider-neutral generation metadata, never conversation text, system instructions, reasoning, response text, API keys, endpoints, raw diagnostics, or telemetry bodies.

Deletion exists for transactional rollback and future lifecycle work; confirmed snapshots are otherwise immutable.

### Assistant reference and persistence ordering

`ChatMessage` gains optional `GenerationSnapshotId`. Only Assistant revisions may carry the reference. The existing message payload hash remains the Lot 10B.5a content hash over logical message identity, role, content, and timestamp; the provenance reference is relational metadata analogous to revision-parent metadata and does not rewrite the v1 message payload-hash contract.

`JsonConversationRepository` advances to schema v4 and persists the optional snapshot reference. Schemas 0, 2, and 3 remain readable; schema v3 revisions naturally load with no generation-snapshot reference.

For successful generation, orchestration uses this order:

1. capture the immutable turn and resolve the generation snapshot;
2. execute the provider request;
3. reload and reject stale conversation history;
4. build a detached updated conversation containing the finalized Assistant revision;
5. persist the generation snapshot;
6. persist the updated conversation;
7. if conversation persistence fails, delete the just-written snapshot before propagating failure.

Provider failure, caller cancellation, empty/invalid streamed completion, or stale-history rejection therefore persists neither the final Assistant revision nor its generation snapshot. A partial stream remains Presentation-only and does not become historical provenance.

A snapshot-bound turn requires an `IGenerationSnapshotStore` before the provider is called; composition cannot silently produce a snapshot reference that has no durable registry.

## Consequences

- Every successfully finalized snapshot-bound Assistant revision retains a stable durable provenance reference.
- The snapshot records the exact ordered message revisions used as provider input without duplicating their text.
- Default-profile generation still has a real profile identity with no synthetic revision; legacy global generation has no synthetic profile identity at all.
- Snapshot persistence and conversation persistence cannot normally leave an orphan snapshot when conversation saving fails.
- Conversation JSON v4 remains backward-readable from v0/v2/v3.
- Domain remains independent from Application because only the typed snapshot identifier crosses the boundary.
- Branch graph/edit semantics remain 10B.5c; context revisions/provenance and explicit context budget remain 10B.6.
