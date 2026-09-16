# ADR 0032 - Immutable generation profile core

## Status

Accepted.

## Context

Lot 10B.1 introduced an immutable `GenerationSnapshot` for the provider-neutral generation state selected when a turn starts. The next Lot 10B capability is generation profiles: reusable user-defined behavior such as Code, Creative, or Short answers, plus an always-available default profile that preserves native provider/model behavior.

The UI specification also requires future persistent working drafts, immutable profile revisions, per-model profile catalogs, conversation-level profile selection, and provenance. Implementing all of those at once would couple storage, revision semantics, Presentation state, and generation execution before their boundaries are established.

A stable immutable profile contract is therefore needed first. It must remain independent from Infrastructure and must not silently invent provider defaults.

## Decision

Introduce `Alicia.Application.Generations.GenerationProfileId` as a typed stable UUID and `GenerationProfile` as an immutable Application contract describing reusable generation behavior.

A custom profile contains:

- stable `GenerationProfileId`;
- normalized non-empty name;
- optional base system instructions;
- a defensive copy of provider-neutral `InferenceGenerationOptions`;
- an immutable snapshot of initial suggestion strings.

The built-in default profile is created explicitly through `GenerationProfile.CreateDefault`. It is marked with `IsDefault`, uses the reserved internal name `Default`, carries no system instructions, no Alicia generation overrides, and no profile suggestions. It therefore preserves the existing provider/model-default semantics. Custom profiles cannot claim that reserved name.

`GenerationProfile` deliberately does not contain provider-specific request data, credentials, runtime state, telemetry, conversation content, message revisions, provenance records, retrieved context, or mutable draft state.

The first profile contract also does not encode model ownership, icon/color presentation metadata, persistence, selection, working drafts, or revision identifiers. The next atomic profile step will introduce the model-scoped catalog/persistence boundary and immutable revision/draft semantics. Keeping model ownership outside the behavioral payload avoids binding profile identity to the current raw model-reference string before Alicia has a stable model catalog contract.

## Consequences

- Generation-profile behavior has a provider-neutral immutable representation before persistence or UI wiring.
- A future model-scoped catalog can guarantee one non-editable/non-deletable default profile per model without changing the profile payload.
- Future profile revisions can retain the same `GenerationProfileId` while adding independent revision identifiers and hashes.
- `GenerationSnapshot` remains unchanged in this step; profile/revision provenance will be attached only when that boundary is introduced deliberately.
- Existing provider configuration, conversation persistence, generation execution, and Presentation behavior remain unchanged in Lot 10B.2.
