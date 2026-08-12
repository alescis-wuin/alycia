# ADR 0012 — Presentation workspace shell

## Status

Accepted.

## Context

The current Avalonia `MainView` and `MainViewModel` expose conversation history, provider lifecycle, model configuration, message composition, streaming state, and local conversation actions from one presentation surface. The UI/UX redesign requires Conversations, Providers, and Models to become distinct workspaces under one persistent global navigation shell.

Splitting the existing `MainViewModel` at the same time as the visual workspace decomposition would mix navigation restructuring with provider, conversation, and streaming behavior changes. That would make regressions difficult to isolate and would duplicate state if each new view constructed its own copy of the current workspace model.

## Decision

Introduce a lightweight Presentation shell before physically splitting the current workspace.

- `WorkspaceSection` defines the global workspace identity: Conversations, Providers, and Models.
- `ShellViewModel` owns the selected global section and exactly one shared `MainViewModel` instance exposed as `Workspace`.
- Conversations is always the initial selected section.
- Shell navigation commands only change shell selection. They do not recreate, initialize, or mutate the shared workspace state.
- `ShellView` becomes the Presentation root and currently hosts the existing `MainView` through the shared `Workspace` property.
- The desktop composition root constructs `ShellViewModel` around the existing `MainViewModel` and configures Presentation with a shell factory.
- `MainWindow` and single-view application lifetimes both use `ShellView` as their root surface.
- This step deliberately does not add the global navigation rail or split Provider/Model controls into separate views. Those follow on top of the shell without changing the current business/runtime contracts.

## Consequences

- The application now has an explicit global navigation state without changing conversation/provider behavior.
- Future Conversation, Provider, and Model views can bind to the same existing workspace instance during migration, preventing duplicate provider runtimes, drafts, selections, or conversation state.
- Existing `MainViewModel` tests remain valid while shell navigation receives focused tests of its own.
- Presentation startup now depends on a `ShellViewModel` factory rather than a `MainViewModel` factory.
- The shell is intentionally thin. Responsibility extraction from `MainViewModel` is deferred until the workspace views exist and can be migrated independently.
