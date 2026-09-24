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

## Snapshot policy

Stage 3D-A generates reviewable current-state PNGs but does not commit image baselines yet. This avoids approving a visual reference without human review and avoids making the normal quality gate dependent on host-specific font rasterization.

A later visual-regression closeout may promote reviewed Linux captures into explicit baselines and compare them with a controlled tolerance. Baseline replacement must remain an explicit operation rather than an automatic side effect of a test run.

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
