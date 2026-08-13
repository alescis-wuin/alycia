# Alicia

Alicia is a cross-platform AI chat application built with C#, .NET, and Avalonia.

## Status

The repository contains the engineering foundation, a provider-neutral conversation core, local JSON conversation persistence, tested conversation lifecycle operations, a functional Avalonia shell with dedicated Conversations/Provider/Models workspaces, provider-neutral response generation/streaming, a real local llama.cpp CUDA adapter, and explicit provider/model generation configuration including bounded reasoning. The desktop UI can detect or install a managed Linux x64 CUDA build, save a Hugging Face GGUF model plus optional runtime/generation overrides, launch `llama-server -hf`, stream visible content and separated reasoning incrementally, persist only the completed visible Assistant message, stop an active stream, and retry the same unanswered User message without duplicating it. Tool calling, retrieval, attachments, multimodality, and additional platform installers/hosts are intentionally deferred to later atomic work packages.

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
- a configured Git identity and GPG signing key before creating signed commits;
- for Alicia-managed llama.cpp CUDA installation on Linux x64: an NVIDIA driver (`nvidia-smi`), CUDA Toolkit (`nvcc`), CMake, a C and C++ compiler, and Ninja or Make. Alicia does not install privileged system packages.

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

- a persistent 56 px icon-only global rail with dedicated **Conversations**, **Provider**, and **Models** workspaces plus a delayed label flyout;
- a Conversation-only history surface with create/select, title-and-content search, lazy three-message previews, contextual `…`/right-click Rename and Delete, persisted collapse state, and automatic narrow-layout collapse;
- a polished conversation thread with transparent history/thread surfaces and an opaque click-to-focus composer as the primary input surface;
- multiline composition with Enter-to-send, Shift+Enter newline, icon-only Send, provider-neutral message persistence, and stale-history protection;
- explicit Provider detect/install/start/stop operations and phase-aware installation progress outside the Conversation sidebar;
- explicit Models configuration for Hugging Face GGUF reference, optional context/generation overrides, reasoning enable/disable, and bounded reasoning tokens;
- no automatic provider fallback: unsaved or unavailable selections are never silently replaced;
- a managed llama.cpp session bound to a dynamically selected IPv4-loopback port with localhost-only CORS, the bundled UI disabled, a per-launch ephemeral API key outside the command line, public `/health` readiness followed by authenticated `/props` ownership verification, and Bearer-authenticated chat streaming;
- OpenAI-compatible SSE streaming with separate visible Content and Reasoning chunks;
- a first-delta thinking indicator with a Reduced Motion path and darker collapsible reasoning-step surface while preserving only visible final Assistant content;
- per-conversation `FOLLOWING`/`DETACHED` scroll state with a 96 px detach threshold, persisted viewport position, and an explicit ↓ return-to-latest action that streaming never overrides while detached;
- short Stop/Retry guard windows, cancellation that discards partial output, and retry of the existing unanswered User message without duplication;
- reasoning kept as an ephemeral in-memory Presentation snapshot rather than silently persisted into conversation documents;
- loading/empty/error states, keyboard accessibility, visible focus, accessible names, responsive spacing, and large interaction targets.

The provider-neutral Application contracts remain independent of llama.cpp/CUDA/HTTP. The managed compiler path is intentionally limited to Linux x64 + NVIDIA CUDA for the current lot; compatible externally installed CUDA `llama-server` executables can also be detected from `PATH`.

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
- `docs/decisions/0010-managed-llama-cpp-cuda-provider.md`
- `docs/decisions/0011-provider-model-configuration.md`
- `docs/decisions/0012-presentation-workspace-shell.md`
- `docs/decisions/0013-global-navigation-rail.md`
- `docs/decisions/0014-conversation-only-workspace.md`
- `docs/decisions/0015-conversation-history-discovery.md`
- `docs/decisions/0016-conversation-surface-polish.md`
- `docs/decisions/0017-reasoning-aware-streaming.md`
- `docs/decisions/0018-secure-local-llama-server-session.md`
- `docs/decisions/0019-conversation-following-ui-state.md`
- `docs/development/roadmap.md`
- `docs/design/ui-ux-specification.md`
- `docs/user-guide/README.md`
- `docs/operations/README.md`
- `docs/development/project-profile.md`
- `docs/local-dotnet-toolchain.md`
- `docs/patch-packages.md`

## Security

Do not commit provider API keys, OAuth tokens, model credentials, local secrets, or conversation exports containing sensitive information. See `SECURITY.md`.

## License

No license has been selected yet. The project owner must choose one before public distribution.
