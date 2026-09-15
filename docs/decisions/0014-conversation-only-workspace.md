# ADR 0014 — Conversation-only MainView

## Status

Accepted for UIX-01 stage 4.

## Context

UIX-01 stage 3 introduced the persistent global rail and dedicated Provider and Models
workspace surfaces, but `MainView` still duplicated provider/model controls inside the
conversation-history sidebar. It also kept legacy presentation chrome that competes
with the conversation itself: application branding, history count/refresh controls,
persistent conversation title/meta, sidebar status text, and permanent per-message
role/timestamp metadata.

Keeping those controls in `MainView` would make the global navigation semantically
ambiguous and would increase the cost of the later context/composer work.

## Decision

`MainView` becomes the Conversation-only presentation surface.

For this intermediate step:

- Provider and model configuration is removed from the conversation sidebar.
- The global Provider and Models workspaces remain the only visible entry points for
  those controls.
- The sidebar is reduced to a compact Conversations heading, a small create action,
  local conversation items, and the existing rename/delete workflows.
- Conversation list metadata is no longer rendered.
- The permanent conversation title/meta block above the thread is removed.
- Message role and timestamp metadata is no longer rendered permanently.
- Existing conversation lifecycle, streaming, Stop, Retry, keyboard shortcuts,
  persistence, and shared `MainViewModel` state are unchanged.

Search, history collapse persistence, overflow/context menus, the final composer,
context panel, hover provenance/actions, and branch/revision UI remain deferred.

## Consequences

The visual and semantic boundary between Conversation, Provider, and Models is now
explicit. Future UIX work can replace individual conversation components without
moving provider/model controls again.

Some transitional conversation interactions remain intentionally unchanged until
their dedicated lots, notably inline rename/delete hover actions and the current
composer status/gating behavior.
