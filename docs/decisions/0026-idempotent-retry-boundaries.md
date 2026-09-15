# ADR 0026 — Explicit generation retry and bounded idempotent transport retry

## Status
Accepted — Lot 10.4.

## Context
A local generation request is a POST whose execution becomes ambiguous once the server may have accepted it. Automatically replaying after a timeout or disconnect can duplicate model work or produce a second answer for the same user turn. In contrast, selected provider operations are observation-only HTTP GETs and can be retried safely when the failure is transient.

## Decision

- Alicia never automatically replays `POST /v1/chat/completions`.
- Conversation retry remains an explicit user action and reuses the persisted unanswered user message instead of appending a duplicate.
- Automatic transport retry is restricted to explicitly identified idempotent HTTP boundaries.
- The llama.cpp implementation uses at most three attempts with a short bounded delay for HTTP 408, 429, 500, 502, 503, 504 and network-class transport failures.
- Permanent responses such as 400/401/403/404 are returned immediately and are not retried.
- Release discovery and the initial source-download request may retry; a source stream that already started is not silently restarted after an idle/read failure.
- Session ownership GET probes may retry transient responses. Readiness itself remains bounded by the existing readiness deadline.
- Caller cancellation has priority over retry and backoff and is never reclassified as a retryable transport failure.

## Consequences

Retry behavior is deterministic and auditable. Idempotent network jitter can recover without user intervention, while generation ambiguity always requires an explicit user decision. Lot 10.5 can build version/update behavior on top of these bounded network semantics without broadening generation replay.
