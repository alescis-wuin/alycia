# Alicia

Alicia is a cross-platform AI chat application built with C#, .NET, and Avalonia.

## Status

The repository contains the engineering foundation, a provider-neutral conversation core, local JSON conversation persistence, tested conversation lifecycle operations, a functional Avalonia conversation workspace, provider-neutral response generation, non-streaming turn completion, and provider-neutral response streaming. The desktop UI can persist a user message, project deterministic assistant output incrementally, persist only the completed assistant message, stop an active stream, and retry the same unanswered user message without duplicating it. External provider adapters, tool calling, retrieval, attachments, and additional platform hosts are intentionally deferred to later atomic work packages.

## Current platform target

The first executable host uses `Avalonia.Desktop`, which targets Windows, Linux, and macOS. The shared `Alicia.Presentation` project is isolated from that host so Android, iOS, and browser hosts can be added later without moving application or domain logic.

## Architecture

```text
Alicia.Desktop ───────→ Alicia.Presentation ───────→ Alicia.Application ───────→ Alicia.Domain
       │                                       └──→ Alicia.Domain
       └──────────────→ Alicia.Infrastructure ─────→ Alicia.Application
                                          └───────→ Alicia.Domain
```

The dependency graph is checked automatically. Presentation code must not depend directly on infrastructure implementations; the desktop executable is the platform composition root that selects and injects those implementations.

## Prerequisites

- Linux, macOS, or Windows for the application itself;
- Bash, Git, Make, Python 3, GPG, and either curl or wget for repository tooling;
- ShellCheck for the complete local quality gate;
- a configured Git identity and GPG signing key before creating signed commits.

The repository pins .NET SDK `10.0.110` in `global.json`. `make toolchain-bootstrap` installs a compatible SDK under `.dotnet/` when needed.

## Common commands

```bash
make toolchain-bootstrap
make hooks-install
make build
make test
make run
make architecture
make dependency-graph
make verify
```

## Conversation workspace

The current Avalonia presentation provides:

- a persistent history sidebar ordered by recent activity;
- conversation creation and selection;
- contextual rename and delete actions revealed on pointer hover or keyboard focus;
- orange edit and red delete icon actions with contextual tooltips and accessible names;
- direct inline renaming that replaces the history title in place, focuses the editor, and selects the current title automatically;
- Enter/Escape and inline save/cancel actions with presentation-side validation before domain persistence;
- explicit two-step deletion confirmation with irreversible-action warning;
- a multiline local message composer with Enter-to-send and Shift+Enter newline behavior;
- user-message persistence through the existing provider-neutral append-message application use case;
- complete non-streaming `User → Assistant` turn orchestration plus an additive streaming turn use case tied to the triggering user-message identifier;
- a provider-neutral `IStreamingConversationResponder` boundary that delivers validated content deltas without replacing the existing non-streaming port;
- a cancellable local development responder that emits deterministic chunks and is selected only by the desktop composition root;
- transient streamed Assistant projection in the workspace, with no partial response written to conversation history;
- Stop and retry actions that discard partial output, preserve the persisted user message, and avoid resending it;
- stale-history detection after streaming and before final assistant persistence so responses generated from changed history are rejected;
- automatic message-list scrolling while streamed content grows plus recent-activity/sidebar refresh after successful completion;
- visually distinct user, assistant, and system message projections with accessible labels;
- message history rendering with role and timestamp projection;
- dedicated loading, no-history, no-selection, and empty-conversation states;
- visible error projection, busy progress, live-region announcements, and refresh controls;
- keyboard shortcuts for creation, refresh, rename, and cancellation;
- responsive sidebar and content spacing driven by Avalonia container queries;
- navigation/main accessibility landmarks, heading metadata, visible keyboard focus, and large control targets;
- a high-contrast dark visual system with distinct selected, editing, success, and destructive states.

Local user-message composition and streamed assistant-response persistence are wired end to end. Partial deltas exist only in Presentation; Application concatenates the completed stream, revalidates conversation history, and persists one immutable Assistant message. The current desktop responder is deliberately deterministic and local so incremental rendering, cancellation, retry, concurrency checks, focus, scrolling, and final-only persistence can be validated before introducing a real model provider.

## Documentation

- `docs/architecture/overview.md`
- `docs/decisions/0001-foundation-architecture.md`
- `docs/decisions/0002-provider-neutral-conversation-core.md`
- `docs/decisions/0003-local-conversation-persistence.md`
- `docs/decisions/0004-conversation-lifecycle.md`
- `docs/decisions/0005-desktop-composition-root.md`
- `docs/decisions/0006-local-message-composition.md`
- `docs/decisions/0007-provider-neutral-response-generation.md`
- `docs/decisions/0008-non-streaming-conversation-turn.md`
- `docs/decisions/0009-provider-neutral-response-streaming.md`
- `docs/development/project-profile.md`
- `docs/local-dotnet-toolchain.md`
- `docs/patch-packages.md`

## Security

Do not commit provider API keys, OAuth tokens, model credentials, local secrets, or conversation exports containing sensitive information. See `SECURITY.md`.

## License

No license has been selected yet. The project owner must choose one before public distribution.
