# ADR 0016 — Conversation surface visual hierarchy and hit targets

## Status

Accepted for UIX-01 stage 6.

## Context

The stage 5 conversation history is functionally complete, but the first desktop smoke test exposed several presentation issues: the collapse glyph reads as text rather than navigation, the create action competes with the history title, card hit targets do not feel full-width, the rail/history boundary is too explicit, search/thread/composer surfaces create unnecessary nested opaque layers, and the message scrollbar can visually collide with right-aligned content.

## Decision

Stage 6 is a presentation-only polish pass. It does not change conversation, provider, model, search, or persistence contracts.

- The history title is centered independently from the collapse control.
- Collapse/reopen and create actions use vector glyphs rather than text characters.
- The create action moves beside the search field and both controls share one row.
- History entries stretch across the available panel width and spacing is made explicit between search/results and between entries.
- The global rail and conversation-history panel no longer draw a vertical separator; hierarchy comes from a small background-tone difference.
- Search uses a transparent field surface inside the history panel.
- The conversation thread shell and message fills are transparent; message outlines and alignment retain role distinction.
- The message list reserves right-side breathing room so the scrollbar does not overlap right-aligned content.
- The composer shell becomes the only deliberately opaque conversation input surface. Its inner text box is transparent and borderless.
- Clicking the composer shell outside child buttons focuses the text editor, and the shell presents an I-beam cursor while buttons keep pointer semantics.
- The persistent composer status line is removed from the visual tree; transient response actions remain available through Stop/Retry.
- Send becomes an icon-only filled triangle on the existing accent-secondary background.

## Consequences

This stage deliberately keeps the existing composer/provider gate, message projection, search implementation, transient history collapse state, and streaming logic. The later context/composer lot can add model/profile/context controls without first undoing nested visual surfaces.
