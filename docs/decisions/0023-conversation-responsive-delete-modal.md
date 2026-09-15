# ADR 0023 — Responsive conversation overlay and delete modal

## Status

Accepted — UIX-01 Stage 10C.

## Context

The Conversation workspace persisted a single expanded/collapsed history preference and automatically collapsed it on narrow layouts. Reopening history while narrow still placed the sidebar in the grid, reducing the usable conversation width. Deletion confirmation was rendered as an inline warning band above the thread, so it displaced content and did not behave like a blocking destructive confirmation.

## Decision

The persisted history preference remains the **wide-layout preference**.

At a Conversation surface width of 900 DIP or below:

- history starts closed;
- opening history creates a temporary 300 DIP overlay above the conversation;
- a backdrop blocks pointer interaction with the conversation and closes the overlay when activated;
- opening/closing the narrow overlay does not write the persisted wide-layout preference;
- selecting the current or another conversation, or creating a conversation, closes the narrow overlay;
- leaving narrow layout restores the persisted wide-layout preference.

The existing container-query system progressively reduces content spacing at 980, 860 and 700 DIP.

Conversation deletion is confirmed by a root-level Presentation modal:

- the modal covers both history and conversation;
- the backdrop blocks background pointer interaction;
- clicking outside does not confirm or implicitly dismiss;
- `Escape` and **Cancel** dismiss;
- **Delete** is the only destructive confirmation action.

No Domain or Application contract is changed.

## Consequences

The Conversation thread retains usable width at the minimum desktop window size while history remains reachable. Narrow history interaction no longer mutates the user's wide-layout preference. Destructive confirmation no longer shifts the thread and cannot leave the conversation target interactable behind the confirmation surface.

Focus trapping, exhaustive automation semantics and final motion behavior remain Stage 10D responsibilities.
