# ADR 0021 — Conversation history visual identity

## Status

Accepted for UIX-01 Stage 10A.

## Context

The conversation history needs a compact visual identity beyond the title so recurring conversations are easier to recognize. The identity is a Presentation concern: it changes how a conversation is displayed, but it does not affect conversation content, generation, persistence semantics, or provider behavior.

Storing icon/color fields inside Domain conversation documents would couple visual preferences to the immutable conversation model and force infrastructure migrations for a UI-only feature.

## Decision

Presentation owns a `ConversationVisualIdentity` keyed by `ConversationId` alongside the existing per-conversation scroll state in `ui-state.json`.

Stage 10A provides:

- a predefined icon catalog (`Conversation`, `Code`, `Idea`, `Study`, `Research`, `Writing`, `Creative`, `Work`);
- a predefined color palette (`Teal`, `Violet`, `Blue`, `Green`, `Orange`, `Rose`, `Slate`);
- the same `Change icon` and `Change color` submenus from right-click and `…` history actions;
- immediate projection in the history row and rename state;
- persistence without selecting the target conversation;
- removal of the persisted identity when the conversation is deleted.

The UI-state document moves from version 1 to version 2. Version 1 remains readable and yields the default `Conversation + Teal` identity for conversations that do not yet have explicit identity data. Invalid identity entries are ignored independently rather than invalidating valid scroll/history state.

The icon/color catalog is intentionally represented by stable enum identifiers in persistence and by Presentation-only glyph/brush mappings in the ViewModel layer. This leaves room for a later richer custom-icon source without changing Domain/Application contracts.

## Consequences

Conversation identity survives navigation and application restart while conversation documents remain provider/UI neutral. Existing installations migrate lazily and safely when the next UI-state save writes version 2.

Custom emoji, arbitrary text symbols, and imported images remain a future extension of this Presentation identity boundary; Stage 10A establishes the persistence and interaction contract first.
