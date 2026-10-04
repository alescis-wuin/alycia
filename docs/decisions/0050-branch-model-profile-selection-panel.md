# ADR 0050 — Branch model/profile selection panel

## Status

Implemented — UIX-03 Stage 5 published as signed commit `e0d8132103c0138b41c68d9e31ef9b6c7733beeb`. Owner-host verification passed 465 tests and 27 captures; the owner confirmed CI success on 2026-10-03.

## Context

Stage 4B was published as signed commit `e2a9cbd36d3fd3599384996e2990f1ec7920d0f1` after owner-host review, the complete local gate and Linux CI run `37017246159` (447 tests, 15 verified PNGs). The composer now has two semantic lines. The first Stage 5 candidate used a full-height right-side drawer. Owner review rejected that placement and verbosity: right-hand actions belong to messages, while this choice belongs to the conversation and should stay near its composer trigger.

## Decision

- Keep `GenerationDualSelectorViewModel`, ADR 0045 persistence boundaries and ADR 0049 readiness guards.
- Anchor a compact panel 8 px above the composer model/profile trigger, aligned with its left edge and contained in the Conversation workspace. Width is capped at 520 px, height at 340 px and available vertical space. Resize and composer reflow preserve the anchor and preview.
- Keep model and profile columns side by side with independent scrolling. Visible text is limited to column headings, model/profile names and a known model author. Do not repeat the full owner/model reference below its name.
- Use circular vector-icon buttons: Close, green branch confirmation and contextual help beside each column heading. Each action has an accessible name and a tooltip; help also opens by click or keyboard activation.
- Mark the effective branch model and profile with the same small branch icon beside their names. Preview selection does not move that marker. Full references, provider/status details, WorkingDraft information, compatibility/library distinctions and empty-list guidance remain available in tooltips and accessibility metadata.
- Do not dim the entire conversation or show permanent explanatory paragraphs, conversation-title repetition, status rows or a textual action footer inside the panel.
- Opening restores the effective branch pair. Move initial focus to the model list, or Close when it is empty. Tab and Shift+Tab cycle inside the panel; Escape, Close and the backdrop cancel the preview. Closing returns focus to the composer trigger only when that trigger is visible and enabled.
- Changing conversation reloads and closes the preview. Leaving the Conversation workspace also cancels it. Neither action writes preview choices.
- `Use for this branch` remains the only confirmation action. It saves the selected branch pair and, only when required, a missing Default catalog. It never saves or loads provider configuration, changes the runtime, imports a library entry or confirms a WorkingDraft.
- Confirmed custom profiles and WorkingDraft indicators remain separate. The existing pre-send WorkingDraft gate still decides which revision is used by a new response.
- The selector remains unavailable during generation, busy operations, profile editing and higher-priority confirmation surfaces.

## Validation

Add real-control headless tests for containment at 720/900/1280/1600 px, resize while open, long labels, independent list scrolling, initial/cyclic/returned focus, Escape/Close/backdrop cancellation, explicit apply, compatibility entries, empty catalogs, compact name/author/icon rendering, custom/WorkingDraft selection, conversation/workspace changes and generation guards. Preserve the existing 15 captures and add an explicit 27-image inventory checked locally and in CI.

## Boundaries

No Domain/Application/Infrastructure schema, automatic model import/loading, model/profile deletion, profile editing, branch navigation, Provider decluttering or application-wide Stage 7 polish is introduced.

## Consequences

Branch intent is easier to inspect without changing execution readiness or persistence. The panel can be reviewed independently, and Stage 6 starts only after its owner-host validation and signed published checkpoint.
