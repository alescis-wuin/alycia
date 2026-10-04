# ADR 0051 — Compact Provider workspace

## Status

Accepted contract — UIX-03 Stage 6 v2 passed the owner gate and native review; v3 metrics/readability refinement candidate.

## Context

Stage 5 was published as signed commit `e0d8132103c0138b41c68d9e31ef9b6c7733beeb`; the owner supplied a passing local gate (465 tests, 27 captures) and confirmed CI success. Native Provider review found small text, repeated names/versions, scattered lifecycle buttons, and inconsistent disclosure widths.

## Decision

- Use a bounded two-column workspace. The left column owns commands; the right column owns one Runtime card (state, version, update results and generation metrics) plus a separate Storage card. Below 1020 logical pixels of workspace width, place information below commands without replacing controls or losing focus/expanded state.
- Merge lifecycle and update actions into one surface, with exactly two action rows. Row one exposes one existing Install/Start/Stop command according to runtime state, followed by a circular vector Detect action. Row two exposes Update and a circular vector Check update action. Existing command guards remain authoritative, including Stop during startup.
- Unknown state presents a disabled Start with detection guidance. Missing/unsupported/repairable fault presents Install; network/model failures remain visible and do not become an installation request. Installing and stopping retain their corresponding action label.
- Display the provider name once in the selector. Display the detected installed version once as a contrasted tag, and the validated target only when an update is available. Do not equate an unchecked update with an up-to-date runtime. External installations remain distinct.
- Remove permanent architectural explanation and obsolete GGUF input guidance. Keep explicit failure messages, progress and maintenance results visible. Use at least 16-point workspace content, 18-point values, 20-point panel titles and a 32-point page title, tooltips and an activatable help button. Disabled-action reasons remain in accessibility metadata and parent tooltips.
- Storage refresh is explicit: inspection walks runtime and cache files and is not free. Opening Provider does not check network updates, inspect storage, install, start, stop or remove anything. Unknown storage uses an em dash rather than invented zero values.
- Maintenance spans the command column and is collapsed by default. Inspection, stopped-runtime guards, separate deletion scopes and explicit confirmation stay intact. Maintenance uses the rounded workspace surface with its native Expander semantics. Generation metrics and their privacy contract live in Runtime.
- Keep business contracts, provider lifecycle implementation, persistence and validated-release policy unchanged.

## Validation

Real-control headless coverage checks four viewports, four action controls arranged in two rows, responsive columns, full-width maintenance, icons/names/tooltips, explicit lifecycle dispatch, update gating, storage/confirmation boundaries, visible operation failures, keyboard focus through resize, and unsaved model configuration. Keep all 39 Stage 6 v2 captures and add nine metrics/help captures; the shared inventory is 48 images. The owner confirmed v2 functional/responsive with no observed regressions before approving v3.

## Structured metrics refinement

- One Last generation heading and an outcome badge (Completed, Cancelled or Failed). No observation shows only No generation yet. Errors keep their classification; unavailable optional metrics are em dashes, not zero.
- Model name and author are separated; the complete reference stays in the tooltip. The current provider name is not repeated in the metrics. A historical runtime tag is shown only when the observed version differs from the installed version; the original observation identity remains in help.
- Input/Output compare provider-reported token counts, prompt/generation durations and rates. Cached input, Alicia total duration and first output are separate rows. Do not sum metrics from different timing scopes.
- Local-culture formatting: three decimals for all displayed counts, seconds, rates and storage sizes, without digit grouping. Monospace numbers align on the decimal separator and have separate unit columns. Formatting does not alter stored observations.
- Fallback : Disabled is a soft amber textual badge; the policy explanation is in a circular vector help button. Runtime, metrics, Storage and Maintenance help are keyboard-activatable; Escape or leaving focus dismisses help.
- Application/business contracts, generation measurements, runtime actions and persistence remain unchanged. The refinement only changes Presentation and its documentation/tests.

## Boundaries

Stage 7 remains application-wide accessibility/responsive/visual polish. This candidate does not add automatic provider switching, model loading, cache scanning or upstream update installation.


### Stage 6 v4 — native review refinement

All workspace numbers use three decimal places in the current culture (including token/release counts as a display convention; underlying types and measurements stay unchanged). No thousands grouping is added. Values use DejaVu Sans Mono, with Consolas/Menlo/monospace fallbacks. Equal fractional widths and right alignment place decimal separators on the same axis within each numeric column. Units remain separate. Headless checks inspect rendered decimal positions and glyph widths, including fr-FR/en-US and large values.

The Input/Output table has five subdued one-pixel separators. Help buttons are 32 px, refresh buttons 38 px, provider selection is dark and rounded, and the disabled fallback policy is soft amber. Native maintenance disclosure and all command guards remain unchanged. The 179 Presentation tests / 490 solution tests and 48-capture inventory are retained.


### Stage 6 v5 — token integers, release states and aligned badges

This refinement supersedes v4 formatting for the Input/Output Tokens row and Old releases only. Tokens are integers; an invisible measured decimal suffix reserves the width of the current-culture separator and three monospaced fractional digits. Their last integer digit aligns with the units digit immediately before the decimal separator in Duration/Rate. Cached input and other decimal rows retain three decimals. Missing token values remain em dashes. The reserved suffix is excluded from the normal accessibility tree.

Old releases shows an amber Unknown badge before inspection and None detected after an inspection reports zero. Positive counts remain integers. A failed refresh does not invent a zero. Runtime, Installed and Fallback share a grid column for their badges, with 16 px text and identical padding/corner treatment; installed and historical version tags retain their turquoise color. Provider state transitions, explicit scanning, maintenance confirmation and persistence remain unchanged.

Headless checks cover integer/decimal glyph positions, four viewports, badge start positions/heights/padding, two cultures, and the Unknown → None detected → positive count transitions. Three new Storage captures extend the inventory to 51; Presentation has 180 tests, solution 491. Native review and the full owner gate remain required before signed publication.

### Stage 6 v6 — balanced Input/Output columns

The runtime version row is labelled Version. No redundant installation-status row is added. Generation table units appear once in the row labels Duration (s) and Rate (tok/s). Input and Output use equal-width numeric blocks centred under their headings, with increased spacing around the vertical separators. The shared width follows the widest rendered value, including the reserved decimal suffix on integer token counts; numbers themselves remain right-aligned within that block. The last token digit therefore remains aligned with the units digit before the decimal separator. Three fractional digits and the monospaced font are retained. Storage and whole-request metrics keep their separate units and existing layout.

This is an owner-approved visual candidate. Native acceptance of v6 and signed publication remain pending.

### Stage 6 v7 — shared unit style and right-aligned values

The owner supersedes the v5/v6 token-alignment choice: every value in Input/Output is now right-aligned to the last displayed digit, with 12 px of trailing cell padding. Tokens remain integers and no longer reserve a fractional suffix or align their last digit before the decimal separator. Duration and rate retain three fractional digits, so their decimal separators continue to align. The unused fractional-placeholder projection is removed.

All seven unit labels in the generation table, whole-request metrics and Storage share a 16 px bold style using the existing violet AccentBrush. Table units remain in the first column next to Duration and Rate; numbers retain their turquoise monospaced style. Version and status badges retain v6 behavior.

Existing viewport tests now assert rightmost glyph positions, decimal-to-decimal alignment, integer token formatting and consistent unit styling across all three regions. Counts remain 180 Presentation / 491 solution / 51 captures. Native v7 acceptance and the owner full gate remain pending. The patch accepts the exact uncommitted v5 or v6 checkpoint and rolls back to whichever preimage was applied.
