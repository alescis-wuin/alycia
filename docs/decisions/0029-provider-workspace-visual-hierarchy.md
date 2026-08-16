# 0029 — Provider workspace visual hierarchy without functional change

## Status

Accepted for UI refinement Lot 10.7A.

## Context

Provider capabilities grew through Lots 10.3–10.6: lifecycle, safe failures, explicit update handling and explicit managed-storage maintenance. Those capabilities are correct, but presenting every section and action with similar visual weight makes the workspace harder to scan.

The project UI/UX specification already requires functional sobriety: primary actions should be obvious, supplemental details should stay secondary, and destructive controls should not compete with the main task.

## Decision

Lot 10.7A changes Presentation markup and shared visual classes only. It does not change provider commands, ViewModel state, Application contracts, runtime behavior, persistence or destructive-operation semantics.

The Provider workspace uses this hierarchy:

1. **Runtime** — selected provider, current state, version, progress and lifecycle actions.
2. **Runtime updates** — managed/validated/upstream release information and explicit update actions.
3. **Maintenance** — collapsed by default because storage inspection and cleanup are secondary to normal provider use.
4. **Danger zone** — visually isolated inside Maintenance; confirmations remain explicit and unchanged.
5. **Technical details** — supplemental configuration information is collapsed by default.

Only **Start** keeps primary-action prominence in Provider. Detect, Install, Stop, Check update and Update validated remain available but use quieter treatments. Destructive actions keep explicit danger styling.

The Models workspace adopts the same calmer surface language and removes the competing second card while preserving every binding and command.

## Accessibility

- existing AutomationProperties names, help text, headings and live regions are preserved;
- keyboard focus remains visible through the existing global focus style;
- destructive actions retain text labels in addition to color;
- controls keep their existing interaction-size floor;
- progressive disclosure uses the native Avalonia Expander control rather than custom pointer-only behavior.

## Responsive behavior

Provider and Models content is centered in a bounded single reading column/surface so the layout no longer depends on two equally prominent top-level cards. Existing global navigation and Conversation responsive behavior are unchanged.

## Consequences

The workspace becomes easier to scan and the normal lifecycle task remains visually dominant. Maintenance requires one disclosure action before its controls are shown, but the underlying provider operations and confirmation workflow are unchanged.

## References

- Avalonia accessibility: https://docs.avaloniaui.net/docs/app-development/accessibility
- Avalonia styles: https://docs.avaloniaui.net/docs/styling/styles
- Avalonia responsive layouts: https://docs.avaloniaui.net/docs/layout/responsive-layouts
- W3C WCAG 2.2: https://www.w3.org/TR/WCAG22/
- Fluent 2 layout: https://fluent2.microsoft.design/layout
- Fluent 2 accessibility: https://fluent2.microsoft.design/accessibility
- Fluent 2 button guidance: https://fluent2.microsoft.design/components/web/react/core/button/usage
