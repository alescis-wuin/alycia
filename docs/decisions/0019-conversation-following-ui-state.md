# ADR 0019 — Conversation following and persistent UI state

## Status

Accepted for UIX-01 Stage 8.

## Context

Stage 7 streamed response deltas into the Conversation surface, but `MainView` scrolled to the end after every message collection change and every visible/reasoning delta. That behavior is convenient while the user is reading the latest output, but it prevents deliberate inspection of earlier content during a long generation.

The conversation-history collapse state was also transient. The UI specification requires scroll mode and position to survive conversation switches and application restarts, while keeping those values outside the conversation aggregate because they are presentation state rather than chat history.

The thinking indicator introduced in Stage 7 also needs a reduced-motion path.

## Decision

Presentation defines two scroll modes:

- `Following` — streamed content may keep the viewport at the latest content;
- `Detached` — streamed content never changes the user's viewport position.

A deliberate upward scroll detaches once the viewport is at least 32 device-independent pixels from the bottom. The user can also detach immediately without scrolling through the centered `↑ Pause auto-scroll` control above the composer.

Scrolling back to the bottom does not implicitly reattach. The only transitions from `Detached` to `Following` are:

- the centered `↓ Resume & jump to latest` action;
- sending a new User message.

`MainView` distinguishes offset-only scroll changes from extent/viewport layout changes. Content growth, conversation reloads and responsive resizing therefore cannot overwrite a detached saved position. While following, scrolling to the end remains conditional and harmless when the latest content is already visible.

Presentation owns a versioned non-domain UI-state snapshot containing:

- the explicit conversation-history expanded preference;
- per-conversation scroll mode and vertical offset.

The desktop composition root supplies `LocalApplicationData/Alicia/ui-state.json` through `JsonConversationUiStateStore`. Writes are atomic and malformed/unsupported state falls back to safe defaults. Deleting a conversation removes its presentation scroll entry.

Narrow layouts auto-collapse the history without rewriting the user's explicit expanded preference. A manual reopen is therefore temporary for that narrow layout unless the user explicitly toggles the persisted preference.

Reduced motion is injected into `MainViewModel` by the Desktop host. Desktop uses a best-effort platform preference probe and supports `ALICIA_REDUCED_MOTION=1|0` as an explicit test/override boundary. When reduced motion is active, the first-delta thinking indicator is static instead of cycling punctuation frames.

## Consequences

Long generations no longer pull users away from earlier content they chose to inspect. The user can suspend following explicitly before scrolling, or detach with a short upward gesture even while content is growing. Scroll intent is isolated per conversation and restored after navigation/restart without modifying Domain/Application persistence contracts.

UI-state persistence remains intentionally separate from conversation documents. Future message revisions, generation provenance and context revisions can therefore evolve without mixing view position into immutable conversation history.

The saved position is a vertical UI offset, so significant font/layout changes may clamp it to the new valid range on restore. Semantic scroll anchors can replace that representation later if required without changing the `Following`/`Detached` state machine.
