# ADR 0017 — Reasoning-aware model configuration and streaming

## Status

Accepted for UIX-01 stage 7.

## Context

Recent llama.cpp chat-completion streams can separate model reasoning from visible
assistant content through `delta.reasoning_content` and `delta.content`. Alicia's
existing adapter only consumed visible content. A reasoning-capable model could
therefore spend time producing valid tokens while the Conversation view remained
blank, making generation appear silently stalled and encouraging premature Stop or
Retry actions.

Reasoning is also a model-generation choice rather than a provider installation
choice. The Models workspace already owns generation controls, so it is the correct
scope for this option.

## Decision

Generation configuration gains two optional values:

- `ReasoningEnabled`;
- `ReasoningBudgetTokens`.

The underlying values remain nullable so older saved provider/model configuration
continues to mean "provider/model default". In the Models UI, an unset historical
value is projected as reasoning disabled and the budget editor proposes 512 tokens.
Saving the settings therefore makes the user's reasoning choice explicit.

For llama.cpp requests:

- reasoning explicitly disabled sends `reasoning_effort: "none"` and
  `thinking_budget_tokens: 0`;
- reasoning enabled sends the selected positive `thinking_budget_tokens` and asks
  llama.cpp for separated reasoning with `reasoning_format: "deepseek"`;
- an unset Application-level reasoning choice emits no reasoning override.

`ConversationResponseChunk` now distinguishes `Content` and `Reasoning`. Application
forwards both kinds to Presentation but accumulates and persists only `Content` into
the immutable Assistant message.

Presentation projects reasoning into a darker collapsible section. Blank-line
boundaries create separate reasoning steps. During streaming the section is expanded;
after successful completion the in-memory reasoning snapshot is attached to the final
Assistant projection and starts collapsed.

Raw reasoning is deliberately not stored in the conversation document in this stage.
It remains an ephemeral Presentation snapshot and disappears after a reload/restart.
Persistent reasoning provenance belongs with the later immutable generation-snapshot
and revision work rather than being silently mixed into ordinary message content.

Before the first response delta arrives, the streaming Assistant projection displays
an animated `Alicia réfléchit.` / `..` / `...` indicator. Stop is disabled for the
first 1.2 seconds of a response, and Retry remains disabled for 700 milliseconds after
an interrupted or failed attempt. Test construction can inject zero or shorter delays
without changing production defaults.

## Consequences

Reasoning-capable models no longer look inactive while emitting only reasoning deltas,
and visible final answers remain cleanly separated from intermediate reasoning.
Users can opt out of reasoning or bound its token cost from Models settings.

The current blank-line step segmentation is intentionally provider-neutral and
heuristic. A future structured reasoning protocol can replace it without changing the
Content/Reasoning chunk distinction.
