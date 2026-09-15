# ADR 0007: Provider-neutral response generation

## Status

Accepted.

## Context

Alicia can now persist local user messages, but the Application layer has no
contract for asking an external response engine to generate a reply. Wiring a
specific SDK or network client directly into Presentation or a conversation use
case would couple orchestration to one provider before error handling,
cancellation, and streaming behavior are designed.

The next boundary must make response generation testable without selecting a
provider or mutating conversation history.

## Decision

Application owns a provider-neutral response port and request/result contracts.

- `IConversationResponder` exposes one asynchronous response-generation
  operation with cancellation.
- `ConversationResponseRequest` captures the conversation identifier, title,
  and a defensive snapshot of the ordered immutable chat messages.
- `ConversationResponse` requires meaningful content but preserves returned text
  exactly; it does not trim, rewrite, or add provider metadata.
- `GenerateConversationResponseUseCase` loads the requested conversation through
  `IConversationRepository`, creates the snapshot, invokes the responder, and
  returns its response.
- The generation use case does not append or persist an assistant message.
  Response persistence belongs to the later orchestration package that owns the
  complete user-request/assistant-response transaction.
- Provider adapters will implement `IConversationResponder` in Infrastructure.
  Application does not reference provider SDKs, credentials, HTTP clients,
  model identifiers, or vendor-specific completion types.
- Cancellation and responder exceptions propagate to callers unchanged. A
  missing responder result is treated as an invalid operation.
- Application tests use a deterministic responder double that records the
  request and returns a fixed response.

## Consequences

Provider selection can be added without changing Presentation or conversation
domain types. Response generation can be unit-tested with deterministic data,
and later orchestration can compose message persistence around the same port.

This decision intentionally does not define streaming chunks, tool calls,
retrieval context, provider configuration, retries, model selection, or
credential storage. Those concerns remain separate capabilities.
