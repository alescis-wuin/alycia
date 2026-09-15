# ADR 0015 — Conversation history discovery and contextual actions

## Status

Accepted for UIX-01 stage 5.

## Context

The Conversation-only workspace established in UIX-01 stage 4 still exposes only a
recent-activity list. The target interaction requires discovery by title and message
content, contextual actions without permanently visible edit/delete buttons, a
short delayed preview, and an independently collapsible history surface.

The persistence port currently exposes summary listing and individual conversation
loading, which is sufficient for a first local implementation without widening the
repository contract prematurely.

## Decision

The conversation-history surface gains:

- a debounced search field bound to `HistorySearchText`;
- case-insensitive ranking of exact title, title prefix, title substring, then message
  content matches;
- content scanning through the existing `LoadConversationUseCase` only for entries
  that did not already match by title;
- a search-specific three-message preview centered around the first content match;
- lazy hover preview loading for normal history items, limited to the three most
  recent messages and displayed after roughly 350 ms;
- one overflow `…` action button and the equivalent right-click context menu for the
  existing Rename and Delete workflows;
- an explicit expanded/collapsed history state and toggle command, with a floating
  reopen affordance when collapsed.

The collapse state is deliberately transient in this stage. The ViewModel seam is
created now so a later UI-preference store can persist the user choice without
changing conversation-domain or persistence contracts.

## Ranking

Search ranking is deterministic:

1. exact title match;
2. title prefix match;
3. title substring match;
4. message-content match.

Ties preserve recent-activity order through the item's `UpdatedAt`.

## Consequences

No new repository API is introduced. Content search currently performs local
conversation loads on demand, which is appropriate for the current JSON-backed
local history and keeps the patch scoped to Presentation.

If history volume becomes large, the search contract can later move behind an
Application search/indexing port without changing the visible interaction model.

Conversation icon customization, persisted collapse preference, full-text indexing,
and branch/revision search remain deferred.
