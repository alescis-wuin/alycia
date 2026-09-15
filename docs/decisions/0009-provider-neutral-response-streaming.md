# ADR 0009: Provider-neutral response streaming

## Status

Accepted.

## Context

Alicia can complete a reliable non-streaming conversation turn, including Stop,
retry, and stale-history protection. A real conversational provider should also
be able to expose response content incrementally so the workspace does not wait
for the full completion before showing useful output.

Streaming introduces a different lifetime from a persisted `ChatMessage`.
Provider deltas may be small, may contain whitespace, and may stop because of
user cancellation, provider failure, or a history conflict. Persisting every
chunk would turn transport-level events into domain history, produce excessive
filesystem writes, and make interrupted responses ambiguous.

The existing `IConversationResponder` contract must also remain useful for
providers and tests that only support complete responses.

## Decision

Application adds an independent `IStreamingConversationResponder` port rather
than changing `IConversationResponder`.

- A stream yields `ConversationResponseChunk` values containing non-empty
  `ContentDelta` strings. Whitespace deltas are valid because providers may use
  them to preserve exact response formatting.
- `StreamConversationTurnUseCase` is tied to an explicit conversation and
  triggering User `MessageId`, using the same trigger and snapshot invariants as
  non-streaming completion.
- Application concatenates deltas but does not mutate the conversation while
  streaming.
- Cancellation, responder failure, an empty/blank completed stream, or a stale
  history conflict persists no Assistant message.
- After the responder completes, Application reloads and revalidates the
  conversation snapshot, then persists exactly one immutable Assistant message
  containing the complete response.
- The non-streaming and streaming turn use cases share internal snapshot and
  trigger validation so their concurrency semantics remain aligned.

Presentation creates a transient mutable Assistant projection only for the
active stream. Each delta updates that projection and drives auto-scroll. The
projection is removed after cancellation or failure. On success, Presentation
reloads the conversation so the transient item is replaced by the persisted
immutable Assistant message and timestamp.

Desktop selects the deterministic `DevelopmentConversationResponder`, which
implements both response ports. Its streaming mode emits fixed-size chunks with
cancellable delays so incremental rendering can be smoke-tested before any
external provider is introduced.

## Consequences

Alicia can display useful response content progressively without storing partial
transport state in the conversation domain or JSON schema. Stop remains
non-destructive, retry continues to reuse the existing User trigger, and a
provider can implement streaming without forcing streaming support onto every
adapter.

The current implementation updates the Presentation projection for every
received chunk while coalescing pending auto-scroll dispatches so high-frequency
deltas do not enqueue one scroll operation each. Provider-specific content
buffering or accessibility-announcement coalescing may still be introduced if
real providers produce deltas frequently enough to affect rendering or assistive
technologies. The JSON repository still has the narrow compare-and-swap race
identified by ADR 0008 between final revalidation and filesystem replacement.

This decision does not add a real model provider, provider credentials, model
selection, token/cost telemetry, automatic network retries, tool calls, RAG, or
partial-response persistence.
