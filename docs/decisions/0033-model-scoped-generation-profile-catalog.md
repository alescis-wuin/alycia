# ADR 0033 - Model-scoped generation-profile catalog, drafts, and revisions

## Status

Accepted.

## Context

Lot 10B.1 introduced the immutable `GenerationSnapshot`; Lot 10B.2 introduced the provider-neutral immutable `GenerationProfile` behavioral payload and stable `GenerationProfileId`.

The profile UX requires more than a reusable payload. Profiles are model-specific in the first version, the built-in default profile must always exist, unconfirmed edits must survive restarts, and every confirmed profile save must create an immutable revision with stable entity identity, parent revision, timestamp, and payload hash. The UI timeline itself can remain deferred, but the data model and persistence boundary must exist before profile selection or generation provenance are wired.

This step must not couple Application to llama.cpp, Avalonia, local filesystem paths, or the current Presentation editor. It also must not mutate a confirmed revision when a working draft changes.

## Decision

Introduce a provider-neutral Application catalog boundary for generation profiles.

### Model scope

`GenerationProfileModelScope` identifies one catalog by normalized `ProviderId + ModelReference`. Model ownership remains outside `GenerationProfile` itself: a stable profile UUID therefore remains independent from the current provider/model reference representation, while the containing catalog establishes the V1 model-specific boundary.

### Immutable revisions

`GenerationProfileRevisionId` is a typed UUID. `GenerationProfileRevision` contains:

- a stable revision UUID;
- the stable `GenerationProfileId` derived from its payload;
- optional parent revision UUID;
- UTC creation timestamp;
- a defensive immutable `GenerationProfile` payload;
- `PayloadHash`.

The payload hash is SHA-256 over an Application-owned deterministic canonical representation tagged `alicia-generation-profile-payload-v1`. The canonical representation includes the profile UUID and every behavioral field in a fixed order. It is an integrity/correlation value, not a signature or credential.

The built-in `Default` profile never produces user revisions because its contract is native provider/model behavior and it is non-editable.

### Persistent working drafts

`GenerationProfileWorkingDraft` is separate from confirmed revision history. It contains a defensive custom-profile payload, an optional base revision UUID, and a UTC update timestamp.

- `BaseRevisionId = null` represents a not-yet-confirmed custom profile.
- An existing profile draft references one of that profile's persisted revisions.
- A stale draft is allowed to remain persisted so user work is not discarded.
- Committing a stale draft is rejected until the caller resolves the conflict against the current confirmed revision.
- Committing a current draft creates a new immutable revision whose parent is the draft base, then removes that draft from the returned immutable catalog.

The draft contract stores structurally valid typed profile state. Presentation-specific transient parsing text is not introduced into Application by this lot.

### Immutable catalog

`GenerationProfileCatalog` owns one `GenerationProfileModelScope`, exactly one built-in default profile, the ordered immutable revision history, and at most one working draft per custom profile.

For each custom profile, persisted revisions form a contiguous linear chain. Current confirmed profiles are derived from the latest revision of each stable `GenerationProfileId`; historical payloads remain untouched. Current confirmed profile names are unique within one model catalog using case-insensitive comparison.

The catalog exposes immutable transformations for replacing/removing a working draft and explicitly committing a working draft. It does not select a profile for a conversation and does not alter generation execution.

### Persistence port and local JSON adapter

Application exposes `IGenerationProfileCatalogStore` with model-scoped load and whole-catalog save operations.

Infrastructure implements `JsonGenerationProfileCatalogStore` as a schema-versioned local JSON document containing independent model catalogs. Writes use a same-directory temporary file followed by replacement, matching the repository's existing atomic JSON-store pattern. The adapter:

- returns `null` when no catalog file or scope exists;
- preserves other model scopes when saving one catalog;
- rejects unsupported schemas and duplicate normalized scopes;
- reconstructs Application contracts on read so all invariants are revalidated;
- recomputes every revision payload hash and rejects mismatches;
- persists the stable default-profile UUID, revision chains, and working drafts.

No Desktop composition or Presentation surface is added in this atomic step. A later lot will choose/create catalogs, persist conversation-level model/profile selection, and attach profile revision provenance to generation snapshots.

## Consequences

- Profile history is reconstructible without replaying patches.
- Confirmed revisions cannot be silently overwritten by later edits.
- Drafts survive persistence independently from confirmed history and can be detected as stale.
- One local store can hold independent catalogs for multiple provider/model scopes.
- The default profile remains always representable without creating fake Alicia overrides or fake revisions.
- Application retains no filesystem, JSON, llama.cpp, or Avalonia dependency.
- Profile selection, generation execution, `GenerationSnapshot` provenance, message revisions, conversation branching, and context budgeting remain outside Lot 10B.3.
