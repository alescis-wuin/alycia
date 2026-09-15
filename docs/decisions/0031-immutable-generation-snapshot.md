# ADR 0031 - Immutable generation snapshot

## Status

Accepted.

## Context

Lot 10B must add generation profiles, message revisions, provenance, conversation branching, and explicit context budgeting before RAG. Those capabilities need a stable representation of the generation state that was selected when a turn started. The existing `ConversationTurnSnapshot` is an internal stale-history guard, while `InferenceProviderGenerationObservation` is execution telemetry. Neither contract should be overloaded with durable generation-input semantics.

The provider configuration contract is already immutable, but its lifetime is global and it can change between turns. Lot 10B therefore needs an explicit per-generation value that can later be attached to a message revision without copying provider-specific request models or conversation content into the snapshot.

## Decision

Introduce `Alicia.Application.Generations.GenerationSnapshot` as an immutable provider-neutral Application contract.

The snapshot records:

- `ConversationId`;
- the triggering user `MessageId`;
- capture time normalized to UTC;
- provider identifier;
- optional model reference;
- optional configured context size;
- a defensive copy of `InferenceGenerationOptions`.

Unset configuration values stay unset. Alicia does not materialize provider defaults into the snapshot because the existing configuration contract intentionally treats null as provider/model default.

The snapshot deliberately excludes message text, reasoning, response text, raw provider requests, runtime diagnostics, generation telemetry, profile identity, revision identity, provenance records, retrieved context, and context-budget accounting. Those are separate Lot 10B concerns and will be attached by later atomic steps.

This first step introduces the contract and its invariants only. It does not change conversation JSON persistence, provider routing, generation execution, or Presentation behavior.

## Consequences

- Later generation profiles can resolve to one immutable snapshot before execution.
- Message revisions can reference a stable provider-neutral generation state without depending on Infrastructure.
- Provenance and context can be attached later without duplicating raw conversation content in the snapshot.
- Existing conversation persistence and provider behavior remain unchanged in 10B.1.
