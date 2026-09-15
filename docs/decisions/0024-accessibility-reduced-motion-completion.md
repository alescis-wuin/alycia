# ADR 0024 — Accessibility and Reduced Motion completion

## Status

Accepted for UIX-01 Stage 10D.

## Context

UIX-01 already provided accessible names, visible focus, keyboard shortcuts and a Reduced Motion path for the streaming thinking indicator. The final foundation pass still needed deterministic focus ownership for temporary surfaces, an Escape priority that cannot accidentally stop generation while a panel is open, assistive state for global/history selection, and motion reduction beyond the thinking indicator.

## Decision

- The narrow conversation-history overlay is a temporary focus region. Opening it moves keyboard focus to history search, Tab/Shift+Tab cycle inside it, Escape closes it before generation cancellation is considered, and closing it restores the previously focused element when possible.
- The conversation-delete confirmation is a blocking focus region. Opening it moves focus to **Cancel**, Tab/Shift+Tab remain inside the dialog, Escape cancels, and closing it restores the previous focus target when possible.
- `CancelTransientAction` handles the delete dialog and narrow-history overlay before falling back to rename cancellation and active-response Stop.
- Global navigation exposes explicit automation item status (`Current workspace` / `Available workspace`). Conversation-history items expose selection, metadata and visual-identity status without relying on color alone.
- Reduced Motion is projected through `ShellViewModel`. Under Reduced Motion the global navigation flyout opens without its normal dwell delay and closes with only a short transfer-safe delay; indeterminate progress indicators become static while their textual/live status remains available. Normal motion keeps the existing timing and progress behavior.
- Major Conversation empty/loading states and destructive confirmation expose heading semantics; progress surfaces expose accessible names and polite textual updates.

## Consequences

The accessibility behavior remains Presentation-owned and does not add Domain/Application contracts. Focus restoration is best-effort because the previous control can disappear while an overlay is open. Reduced Motion changes presentation timing/animation only; it never changes provider, streaming, persistence or generation semantics.

## Verification

Automated tests cover Escape precedence, Reduced Motion projection through the shell, navigation automation status and conversation-history accessibility status. The Stage 10D smoke test additionally verifies keyboard focus trapping/restoration and both forced Reduced Motion modes on the desktop host.
