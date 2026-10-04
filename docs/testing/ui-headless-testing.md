# Deterministic headless UI testing

## Purpose

Alycia uses Avalonia Headless for repeatable Presentation QA without manually resizing a desktop window or reproducing keyboard/mouse actions by hand.

The headless suite complements the existing ViewModel tests. It exercises the real XAML control tree, layout, bindings, focus, keyboard input, mouse-wheel scrolling and rendered frames while keeping provider/network behavior replaced by deterministic test doubles.

## Commands

Run the Presentation test project, including headless layout/input checks:

```bash
make ui-test
```

Run the same checks and emit deterministic PNG captures plus SHA-256 sidecars:

```bash
make ui-snapshots
```

Artifacts are written under:

```text
artifacts/ui-tests/
```

The directory is ignored by Git. Snapshot generation never updates approved reference material automatically.

## Current Models matrix

UIX-03 Stage 3D-A covers the Models workspace at these deterministic window sizes:

- 720 x 560 (desktop minimum);
- 900 x 700;
- 1280 x 820 (default desktop window);
- 1600 x 900.

It also exercises:

- empty-library rendering;
- forward/backward keyboard focus traversal;
- ListBox keyboard selection;
- activation of `Use model settings` from the keyboard;
- the loading-draft update boundary;
- vertical scrolling at the minimum viewport;
- the provider-running editability guard;
- screenshot generation from the Skia headless renderer.

The suite deliberately does not infer business state from pixels. Existing ViewModel tests remain authoritative for persistence, provider switching, branch binding and other business invariants.

## Current Conversation composer matrix

UIX-03 Stage 4B extends the headless suite with Conversation composer coverage at the same deterministic desktop matrix:

- 720 x 560;
- 900 x 700;
- 1280 x 820;
- 1600 x 900.

The suite verifies that:

- the message field and Send/Stop/Retry occupy the first semantic line;
- the model/profile selector and branch-readiness guidance occupy the second semantic line;
- both lines remain horizontally contained at every tested viewport;
- keyboard traversal proceeds from the message field to Send and then to the branch model/profile selector when no other response action is visible;
- a branch-model mismatch keeps the composer and selector visible while Send remains disabled;
- the global ADR 0022 configuration gate still replaces the composer when provider/model setup is unavailable.

Snapshot mode also emits `conversation/composer-ready-*.png`, `conversation/composer-mismatch-720x560.png`, `conversation/composer-keyboard-720x560.png`, and `conversation/configuration-gate-900x700.png` for explicit review.

## Snapshot policy

Stage 3D-A generates reviewable current-state PNGs but does not commit image baselines automatically.

Stage 3D-B reviewed the Linux/Skia capture matrix and deliberately keeps those images as review artifacts rather than repository baselines. Structural layout/input assertions therefore remain part of the deterministic test suite without making the normal quality gate depend on host-specific font rasterization.

A future baseline promotion requires a pinned CI OS image, font set, DPI/render scale and explicit image-difference tolerance. Baseline replacement must remain an explicit operation rather than an automatic side effect of a test run.

## Stage 3D-B reviewed matrix

The reviewed Stage 3 artifact set contains:

- `empty-720x560.png`;
- `empty-900x700.png`;
- `empty-1280x820.png`;
- `empty-1600x900.png`;
- `keyboard-720x560.png`;
- `populated-use-settings-900x700.png`;
- `provider-running-900x700.png`;
- `scrolled-720x560.png`.

Review evidence:

- archive: `alycia-uix03-stage3d-a-visual-artifacts-20260924-103200.zip`;
- archive SHA-256: `7035711861a3ab2086abf29ae9798a13b7c981a13845aafa117dddc84cb52862`;
- published harness checkpoint: `d5f61f744fc9972472b4fda30818437c7652af43`;
- automated verification: 439/439 solution tests and 5/5 architecture tests;
- no blocking layout, keyboard-focus, scrolling or provider-running regression observed.

Non-blocking visual observations are retained for UIX-03 Stage 7: minimum-width header copy density/truncation, tight header/provider spacing around 900x700, and clipping of part of the long library-save action label in the minimum-width scrolled capture.

Stage 3 closes without promoting PNG baselines. The headless suite remains the repeatable regression surface and snapshots remain explicit review artifacts.

## CI expectations

The headless tests require no X11/Wayland display server. Rendering uses Avalonia Headless with Skia and `UseHeadlessDrawing = false` so `CaptureRenderedFrame()` produces pixels.

For stable future baseline comparison, CI should keep the following fixed:

- Avalonia/Skia package versions;
- OS image;
- font packages;
- DPI/render scale;
- test viewport sizes.

## Boundaries

Headless tests validate Avalonia's in-process control tree. They do not replace platform-level accessibility or window-manager validation. Native accessibility-tree/E2E checks can be added later with an OS automation layer where platform support is sufficiently stable.

## Dispatcher ownership and recovery validation

The identity palette is shared by plain ViewModel tests and Headless rendering.
It must not retain mutable Avalonia objects owned by the Dispatcher of its first
caller. `ConversationIdentityBrushTests` exercises all fourteen brushes on a
different thread and checks their original colors, opacity and transforms.

Keep `AvaloniaTestIsolationLevel.PerAssembly` and
`CollectionBehavior(DisableTestParallelization = true)` for Presentation. Test
modules are separate executables: no solution-wide serialization is needed for
this palette fix. Every test window is closed in `finally` to avoid retaining
failed scenarios in the shared application.

The published Stage 4B recovery checkpoint contained 136 Presentation tests and
447 solution tests, including 5 architecture tests, with 15 validated PNGs.
Stage 5 extends this inventory as listed below. The shared artifact checker
validates the current PNG count, configured viewports and SHA-256 sidecars.
`make ui-snapshots` and `make verify` enforce this inventory; CI enables captures
in the solution run, applies the same checker and retains captures for review.

The Develop workflow also runs on pushes to `feature/conversation-response`,
so the active implementation receives a check before promotion to Develop.
Artifacts remain review evidence, not approved pixel baselines.

## Current branch selection panel matrix

UIX-03 Stage 5 keeps all 15 previous images and adds 12 under `selector/`:

- `panel-ready-720x560.png`, `panel-ready-900x700.png`, `panel-ready-1280x820.png`, `panel-ready-1600x900.png`;
- `compact-1600x900.png`;
- `panel-keyboard-720x560.png`;
- `confirmed-mismatch-900x700.png`;
- `custom-working-draft-900x700.png`;
- `compatibility-900x700.png`;
- `empty-720x560.png`;
- `long-labels-720x560.png`;
- `scrolled-720x560.png`.

The candidate inventory is 154 Presentation tests and 465 solution tests, including 5 architecture tests. The shared checker now requires exactly 27 PNGs with the same viewport and SHA-256 checks used by CI.

Owner review of v1 led to a compact panel anchored above the composer trigger. The compact-catalog test verifies only headings, names and authors are visible, all four action/help buttons contain vector icons with circular bounds and tooltips, and effective branch markers remain visible. The viewport matrix checks the 8 px anchor gap and 520 px width. Empty guidance is available through help rather than permanent text.

Panel tests verify initial/cyclic/returned focus, Escape/Close/backdrop cancellation, explicit branch apply and missing Default-catalog creation, custom confirmed profiles with an unchanged WorkingDraft, model compatibility outside the library, empty guidance, independent list scrolling, conversation/workspace changes, active-generation guards and resize without discarding a preview. Window cleanup remains unconditional. Provider configuration/library/runtime counters are asserted unchanged during inspection, cancellation and branch confirmation.

Owner-host native review and publication of Stage 5 remain required. Linux headless proof does not replace keyboard/window-manager review or later Windows/macOS promotion checks.


## Provider workspace — UIX-03 Stage 6

The Stage 5 checkpoint passed 154 Presentation tests / 465 solution tests. Stage 6 adds real-control Provider coverage: four viewports, two action rows, responsive columns, full-width Maintenance, lifecycle transitions, validated update gating, explicit storage refresh and uninstall confirmation, visible detect/update/storage failures, keyboard focus through resize and unsaved-model startup guard.

Stage 6 v2 passed with 168 Presentation tests and 479 solution tests, including 5 architecture tests.

The shared checker retains all 27 prior captures and requires 12 more under `provider/`: `ready-{720x560,900x700,1280x820,1600x900}`, `after-{install,start,stop}-1280x820`, `update-1280x820`, `maintenance-1280x820`, and `failure-{detect,update,storage}-1280x820`. All 39 PNGs require exact viewport dimensions and SHA-256 sidecars. Windows are closed unconditionally.

Opening Provider never triggers an update check or storage scan. Assertions use stub operation counters; native review remains required for the actual engine lifecycle and the owner desktop's font/DPI.


### Stage 6 v3 — structured runtime metrics

The current inventory is 179 Presentation tests and 490 solution tests, including five architecture tests. Retain the 39 v2 captures and add nine: `provider/metrics-{720x560,900x700,1280x820,1600x900}.png`, `metrics-{completed,cancelled,failed}-1280x820.png`, `metrics-large-1120x820.png` and `metrics-help-1280x820.png`. The checker requires all 48 PNGs, viewport dimensions and SHA-256 sidecars.

Eleven added cases cover populated layouts, aligned number/unit columns, tabular figures and no horizontal clipping, both fr-FR/en-US decimal/group separators, updates from an initially absent observation, historical version tags, missing versus zero values, cancelled/failed outcomes, long references and large values across the responsive breakpoint, and keyboard help/native Maintenance expansion. The stored generation contract is unchanged. New locale tests restore their culture and every window is closed in finally.

Run the normal UI snapshots in fr-FR and the complete solution in en-US before shipping. Owner native review remains required for desktop DPI, rendering and actual generation. The v2 owner gate and native review already passed; v3 remains a refinement candidate until its owner gate and visual acceptance.


### Stage 6 v4 — native review refinement

All workspace numbers use three decimal places in the current culture (including token/release counts as a display convention; underlying types and measurements stay unchanged). No thousands grouping is added. Values use DejaVu Sans Mono, with Consolas/Menlo/monospace fallbacks. Equal fractional widths and right alignment place decimal separators on the same axis within each numeric column. Units remain separate. Headless checks inspect rendered decimal positions and glyph widths, including fr-FR/en-US and large values.

The Input/Output table has five subdued one-pixel separators. Help buttons are 32 px, refresh buttons 38 px, provider selection is dark and rounded, and the disabled fallback policy is soft amber. Native maintenance disclosure and all command guards remain unchanged. The 179 Presentation tests / 490 solution tests and 48-capture inventory are retained.


### Stage 6 v5 — token integers, release states and aligned badges

This refinement supersedes v4 formatting for the Input/Output Tokens row and Old releases only. Tokens are integers; an invisible measured decimal suffix reserves the width of the current-culture separator and three monospaced fractional digits. Their last integer digit aligns with the units digit immediately before the decimal separator in Duration/Rate. Cached input and other decimal rows retain three decimals. Missing token values remain em dashes. The reserved suffix is excluded from the normal accessibility tree.

Old releases shows an amber Unknown badge before inspection and None detected after an inspection reports zero. Positive counts remain integers. A failed refresh does not invent a zero. Runtime, Installed and Fallback share a grid column for their badges, with 16 px text and identical padding/corner treatment; installed and historical version tags retain their turquoise color. Provider state transitions, explicit scanning, maintenance confirmation and persistence remain unchanged.

Headless checks cover integer/decimal glyph positions, four viewports, badge start positions/heights/padding, two cultures, and the Unknown → None detected → positive count transitions. Three new Storage captures extend the inventory to 51; Presentation has 180 tests, solution 491. Native review and the full owner gate remain required before signed publication.

### Stage 6 v6 — centred numeric blocks

The existing four-viewport metrics checks now assert that Input/Output numeric blocks have the same width and are centred under their headings, that units appear in the row labels, and that the runtime version label is Version. Glyph-level integer/decimal alignment, monospaced digit checks, culture coverage, large values, missing values, badge consistency and storage spacing remain covered. The test and capture counts stay at 180 Presentation / 491 solution / 51 PNGs. Captures use deterministic fixture data; native owner review is still needed.

### Stage 6 v7 — shared unit style and right-aligned values

The owner supersedes the v5/v6 token-alignment choice: every value in Input/Output is now right-aligned to the last displayed digit, with 12 px of trailing cell padding. Tokens remain integers and no longer reserve a fractional suffix or align their last digit before the decimal separator. Duration and rate retain three fractional digits, so their decimal separators continue to align. The unused fractional-placeholder projection is removed.

All seven unit labels in the generation table, whole-request metrics and Storage share a 16 px bold style using the existing violet AccentBrush. Table units remain in the first column next to Duration and Rate; numbers retain their turquoise monospaced style. Version and status badges retain v6 behavior.

Existing viewport tests now assert rightmost glyph positions, decimal-to-decimal alignment, integer token formatting and consistent unit styling across all three regions. Counts remain 180 Presentation / 491 solution / 51 captures. Native v7 acceptance and the owner full gate remain pending. The patch accepts the exact uncommitted v5 or v6 checkpoint and rolls back to whichever preimage was applied.
