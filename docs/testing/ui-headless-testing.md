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

At the Stage 4B recovery checkpoint, expect 136 Presentation tests and 447 tests
for the solution, including 5 architecture tests. The palette regression adds
one test but no screenshot. `scripts/checks/ui-artifacts.py` requires exactly
15 PNGs, the configured viewport dimensions and matching SHA-256 sidecars.
`make ui-snapshots` and `make verify` enforce this inventory; CI enables captures
in the solution run, applies the same checker and retains captures for review.

The Develop workflow also runs on pushes to `feature/conversation-response`,
so the active implementation receives a check before promotion to Develop.
Artifacts remain review evidence, not approved pixel baselines.
