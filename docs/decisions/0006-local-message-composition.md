# ADR 0006: Local message composition

## Status

Accepted.

## Context

The conversation core and JSON persistence already expose `AppendMessageUseCase`,
but the Avalonia workspace can only inspect existing messages. Alicia needs a
complete local write path before introducing any automated response provider.

The presentation must preserve the existing dependency boundaries: Presentation
may invoke Application use cases, while Desktop remains responsible for choosing
the local runtime and Infrastructure implementation.

## Decision

Add local user-message composition to the existing
`feature/conversation-presentation` slice.

- Desktop injects `LocalConversationRuntime.AppendMessage` into `MainViewModel`.
- Presentation owns the transient message draft and exposes a provider-neutral
  `SendMessageCommand`.
- Sending always appends a `MessageRole.User` message through
  `AppendMessageUseCase`; Presentation never writes conversation files directly.
- The draft is cleared only after the append operation succeeds. If persistence
  reports an error, the draft remains available for retry.
- Switching to a different conversation clears the draft to prevent accidental
  cross-conversation sends. Reloading the same conversation keeps the draft.
- After a successful append, the conversation list is reloaded with the same
  preferred conversation so message count, activity time, ordering, and message
  projection remain consistent with persistence.
- Enter submits a message; Shift+Enter inserts a newline. View code-behind is
  limited to keyboard focus and scrolling concerns.
- Message bubbles visually distinguish user, assistant, and system roles while
  retaining text labels and automation metadata so color is not the only signal.
- No automated/model response is generated in this decision.

## Consequences

The application now has a fully testable local conversation write loop before
provider orchestration is introduced. Provider integration can later consume
the same persisted conversation state without changing the composer contract.

Reloading the selected conversation after each successful append favors
consistency and simplicity over incremental Presentation mutation. If profiling
later shows this to be expensive, the list projection can be optimized without
changing the Application boundary.
