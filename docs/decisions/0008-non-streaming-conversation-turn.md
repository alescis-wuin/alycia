# ADR 0008: Non-streaming conversation turn orchestration

## Status

Accepted.

## Context

Alicia can persist user messages and can request provider-neutral responses, but
the two operations are intentionally separate. The desktop workspace therefore
has no complete user-to-assistant turn, no cancellation control, and no retry
path that reuses an already-persisted user message.

A response may also take long enough for conversation state to change after the
generation snapshot is captured. Persisting a reply against an old aggregate
would risk overwriting newer history or attaching a response to stale context.

## Decision

Application owns a `CompleteConversationTurnUseCase` that completes one
non-streaming response for an explicitly identified user message.

- The caller supplies both `ConversationId` and the triggering `MessageId`.
- The trigger must exist, have role `User`, and be the latest unanswered message.
- The use case captures an immutable semantic snapshot before invoking
  `IConversationResponder`.
- Responder failure or cancellation leaves the already-persisted user message
  untouched and appends no assistant message.
- After generation, the conversation is reloaded and its title, timestamps, and
  ordered messages are compared with the captured snapshot.
- If the history changed, the generated response is discarded and the caller
  receives a conflict-style `InvalidOperationException`; the newer history is
  not overwritten by the stale aggregate.
- Only a successful, still-current response becomes a new immutable
  `ChatMessage` with role `Assistant`, timestamped through `TimeProvider`.
- Reusing the same trigger after an assistant message has been persisted is
  rejected, preventing a duplicate completion through this use case.

Presentation persists the user message first, refreshes it into the workspace,
then completes the response while normal conversation interactions are disabled.
The active generation owns a `CancellationTokenSource` exposed through a Stop
command. Cancellation and responder failures preserve a retry target. Retry
calls the completion use case for the existing user-message identifier and does
not append the user message again.

Desktop currently selects `DevelopmentConversationResponder`, a deterministic
Infrastructure adapter with a small cancellable delay. This exists only to
exercise the complete UX and persistence flow before a real provider is chosen.

## Consequences

The first complete conversation turn is testable without a network provider.
User input is durable before generation starts, cancellation is non-destructive,
retry is idempotent with respect to the user message, and responses generated
from history that changed before the final reload are rejected.

The current JSON repository still has no cross-process compare-and-swap
primitive. Reload-and-compare protects the application orchestration from
changes observed before the final save, but a mutation racing strictly between
that comparison and the filesystem replacement remains a future concurrency
hardening concern.

This decision does not introduce streaming, provider credentials, model
selection, automatic network retries, tool calls, retrieval, or partial-message
persistence.
