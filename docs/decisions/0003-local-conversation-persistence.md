# ADR 0003: Local conversation persistence

## Status

Accepted.

## Context

The conversation core defines persistence as an application port and must remain independent from storage technology. The first executable host needs a portable local implementation before provider integration or presentation workflows can rely on durable conversation state.

## Decision

Implement `JsonConversationRepository` in `Alicia.Infrastructure`.

Each conversation is stored as one JSON document named from its typed identifier. Saves serialize to a unique temporary file in the target directory, flush it, and replace the destination file only after serialization succeeds. Loads reconstruct a fresh domain aggregate so callers cannot mutate stored state without an explicit save.

Add `LocalConversationRuntime` as a small manual composition object. It creates one repository instance and injects it, together with a `TimeProvider`, into the existing conversation use cases. Platform hosts remain responsible for selecting the actual application-data directory.

## Consequences

- Domain and Application remain storage-agnostic.
- Persistence uses only the .NET base class library and has no database dependency.
- Stored documents are easy to inspect, export, migrate, and test.
- A malformed document is surfaced as `InvalidDataException` instead of leaking serialization details into callers.
- Repository reads return detached aggregates, preserving explicit save semantics.
- The initial implementation serializes one full conversation per save; indexing, listing, migrations, encryption, and database-backed persistence remain later concerns.
