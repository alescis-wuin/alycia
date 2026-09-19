# Alicia

Alicia is a cross-platform AI chat application built with C#, .NET, and Avalonia.

## Status

The repository contains the engineering foundation, a provider-neutral conversation core, local JSON conversation persistence, tested conversation lifecycle operations, a functional Avalonia shell with dedicated Conversations/Provider/Models workspaces, provider-neutral response generation/streaming, a real local llama.cpp CUDA adapter, explicit provider/model generation configuration including bounded reasoning, and bounded provider failure handling with safe `Missing/Unsupported/Faulted/Network/Model` classification. The desktop UI can detect or install a managed Linux x64 CUDA build, save a Hugging Face GGUF model plus optional runtime/generation overrides, launch `llama-server -hf`, stream visible content and separated reasoning incrementally, persist only the completed visible Assistant message, stop an active stream, and explicitly retry the same unanswered User message without duplicating it. Automatic transport retry is restricted to bounded idempotent provider GET boundaries; generation POSTs are never replayed automatically. Managed llama.cpp installation/update targets an Alicia-validated pinned release and exposes explicit update checking. Managed storage maintenance can inspect usage, clean inactive retained releases, uninstall runtime files while preserving the model cache, or explicitly remove runtime plus model cache; every destructive scope requires separate confirmation and never touches provider configuration or conversations. Provider-neutral generation observability records only bounded metadata for the latest generation and a small rotating local JSONL history: provider/model/version, end-to-end and first-output latency, token usage, llama.cpp timing metadata when present, cancellation, and safe failure classification. Prompt, reasoning, response, raw HTTP, API-key, endpoint, and diagnostic content are not written to that observation log. Tool calling, retrieval, attachments, multimodality, and additional platform installers/hosts are intentionally deferred to later atomic work packages.

## Current platform target

The first executable host uses `Avalonia.Desktop`, which targets Windows, Linux, and macOS. The shared `Alicia.Presentation` project is isolated from that host so Android, iOS, and browser hosts can be added later without moving application or domain logic.

## Architecture

```text
Alicia.Desktop ───────→ Alicia.Presentation ───────→ Alicia.Application ───────→ Alicia.Domain
       │                                       └──→ Alicia.Domain
       └──────────────→ Alicia.Infrastructure ─────→ Alicia.Application
                                          └───────→ Alicia.Domain
```

The dependency graph is checked automatically. Presentation code must not depend directly on infrastructure implementations; the desktop executable is the platform composition root that selects and injects those implementations. `MainViewModel` is now an orchestration/compatibility facade over dedicated conversation-workspace, history, stream, provider, model, generation-settings, and Conversation configuration-gate ViewModels.

Lot 10B.5c adds a durable immutable-prefix conversation branch graph below Presentation. Editing a confirmed User message creates a child branch and a new revision of the same logical `MessageId`; the original branch and its downstream generation provenance remain intact. `Conversation.Messages` stays the linear compatibility view of the active branch, while visual branch navigation/editing remains a later Presentation concern.

Lot 10B.6 adds immutable conversation-context revisions and branch-timeline bindings. A fork pins the exact context effective at its divergence point, later parent context changes cannot rewrite child state, and generation snapshots capture the exact context revision plus an explicit context-window/output-reservation budget when those limits are configured. Unknown provider/model defaults remain unknown; RAG, assets, tools, multimodal input, visual context editing, and tokenizer-specific accounting remain later concerns.

The first UIX-02/03 backend bridge adds an explicit branch-aware generation-selection capability without breaking the Lot 10B.4 conversation-scoped contract. A fork pins the model/profile selection resolved from its parent at fork creation time into the new child branch, branch-aware generation resolves against the active branch when the store supports that capability, and schema-v1 selections remain a backward-compatible fallback. Exact reconstruction of a model/profile selection that was active at an older historical divergence boundary remains a separate revision/timeline concern.

UIX-02 Stage 1 exposes that foundation in the Models workspace without adding profile mutation. Confirmed profiles for the saved provider/model scope can be inspected and explicitly pinned to the active conversation branch. A missing catalog projects a transient `Default` option but is persisted only when the user explicitly saves the branch selection; legacy conversation-scoped selections remain visible as fallbacks until explicitly pinned. Full model-library dual selection, profile editing/revision history, branch navigation, and context UI remain later stages.

UIX-02 Stage 2 adds the first custom profile editor on top of the existing model-scoped catalog. New or existing custom profiles can be persisted as explicit local `WorkingDraft`s, committed as immutable revisions, restored after reload, or discarded back to the latest confirmed revision; stale drafts are never silently rebased. The built-in `Default` profile remains read-only and provider/model defaults remain nullable. Draft autosave, the pre-send previous/modified-profile gate, full revision-history UI, model-library dual selection, branch navigation, and context UI remain later stages.

UIX-02 Stage 3 turns those WorkingDrafts into debounced local autosaves and adds a pre-send revision gate for the profile actually resolved by the active conversation branch. `Use previous version` keeps the draft untouched and generates from the latest confirmed revision; `Use modified version` commits the persisted draft as a new immutable revision before the user message is appended. Stale drafts cannot be confirmed through the gate, and draft editing/autosave can continue during a stream without changing the revision already captured by that response. Revision-history UI, profile deletion, the full model/profile dual selector, branch navigation, and context UI remain later stages.

UIX-02 Stage 4 exposes the immutable confirmed revision chain for the selected custom profile in Models. Older revisions can be restored only as a new WorkingDraft based on the current confirmed head; confirming that draft appends a new immutable revision rather than rewinding history. Existing WorkingDrafts block historical restore so autosaved local work is never overwritten silently. Profile deletion, visual revision comparison, the full model/profile dual selector, branch navigation, and context UI remain later stages.

UIX-02 Stage 5 adds a provider-neutral persistent model-library foundation without changing which model is active. Saved provider/model settings can be explicitly added or updated in `providers/models.json`, listed in Models, and copied back into the editable settings. Library reuse never saves, starts, loads, switches provider, or changes conversation bindings implicitly. The full branch-scoped model/profile dual selector remains the next UIX-02 stage.

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
- a Conversation-only history surface with create/select, title-and-content search, lazy three-message previews, persisted per-conversation icon/color identity, contextual `…`/right-click Rename/Change icon/Change color/Delete actions, a persisted wide-layout collapse preference, a temporary narrow-layout history overlay, and a blocking compact delete modal;
- a polished conversation thread with transparent history/thread surfaces and an opaque click-to-focus composer as the primary input surface;
- a deterministic Conversation configuration gate that keeps history readable while provider/model setup is incomplete, replaces the unavailable composer with one recommended CTA, and becomes central onboarding when no conversations exist;
- multiline composition with Enter-to-send, Shift+Enter newline, icon-only Send, provider-neutral message persistence, and stale-history protection;
- explicit Provider detect/install/start/stop operations and phase-aware installation progress outside the Conversation sidebar;
- a visually simplified Provider workspace that keeps Runtime primary, de-emphasizes update/maintenance detail, isolates destructive actions without changing provider behavior, and keeps latest-generation observability inside collapsed Technical details;
- explicit managed-provider storage inspection plus separately confirmed inactive-release cleanup, runtime-only uninstall, and runtime-plus-model-cache uninstall;
- explicit Models configuration for Hugging Face GGUF reference, optional context/generation overrides, reasoning enable/disable, and bounded reasoning tokens;
- a persistent provider-neutral model library that can retain multiple saved model configurations without implying they are loaded;
- no automatic provider fallback: unsaved or unavailable selections are never silently replaced;
- a managed llama.cpp session bound to a dynamically selected IPv4-loopback port with localhost-only CORS, the bundled UI disabled, a per-launch ephemeral API key outside the command line, public `/health` readiness followed by authenticated `/props` ownership verification, and Bearer-authenticated chat streaming;
- OpenAI-compatible SSE streaming with separate visible Content and Reasoning chunks;
- a first-delta thinking indicator with a Reduced Motion path and darker collapsible reasoning-step surface while preserving only visible final Assistant content;
- completed UIX-01 accessibility behavior with focus-contained temporary history/delete surfaces, Escape-first transient dismissal, assistive current-workspace/history-item status, and Reduced Motion applied to navigation timing and indeterminate progress;
- per-conversation `FOLLOWING`/`DETACHED` scroll state with a low 32 px gesture threshold, persisted viewport position, an explicit ↑ pause control, and a ↓ resume-and-jump-to-latest control that streaming never overrides while detached;
- short Stop/Retry guard windows, cancellation that discards partial output, explicit retry of the existing unanswered User message without duplication, and no automatic generation replay;
- reasoning kept as an ephemeral in-memory Presentation snapshot rather than silently persisted into conversation documents;
- loading/empty/error states, keyboard accessibility, visible focus, accessible names, responsive spacing, and large interaction targets.

The provider-neutral Application contracts remain independent of llama.cpp/CUDA/HTTP. UI refinement Lot 10.7A changes only Presentation markup/styles and preserves the full provider command/binding surface while reducing visual noise. Lot 10.3 adds provider-neutral failure classification and safe-message exceptions while Infrastructure owns finite readiness/HTTP/probe deadlines and raw diagnostics. Lot 10.4 constrains automatic retry to explicitly idempotent GET boundaries while keeping generation retry user-driven. Lot 10.5 adds the optional provider-neutral update capability, pins managed llama.cpp to validated release `b10435` / commit `9e40df63ba151d771d8b247ac4011cf203337e99`, reports upstream latest separately, and atomically activates only an explicitly requested validated update while retaining older managed releases. Lot 10.6 adds the optional provider-neutral maintenance capability with explicit storage inspection, inactive-release cleanup, runtime-only uninstall, and runtime-plus-model-cache uninstall. Destructive maintenance requires confirmation, refuses implicit provider stop, and is constrained to Alicia-owned provider paths without following symlinks. Lot 10.7 adds optional provider-neutral generation observability, requests streamed token usage from llama.cpp, captures provider timings when emitted, records completion/cancellation/safe failure classification, and writes only content-free structured metadata to a bounded rotating local JSONL log. The managed compiler path is intentionally limited to Linux x64 + NVIDIA CUDA for the current lot; compatible externally installed CUDA `llama-server` executables can also be detected from `PATH`.

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
- `docs/decisions/0020-presentation-viewmodel-decomposition.md`
- `docs/decisions/0021-conversation-history-visual-identity.md`
- `docs/decisions/0022-conversation-configuration-gate.md`
- `docs/decisions/0023-conversation-responsive-delete-modal.md`
- `docs/decisions/0024-accessibility-reduced-motion-completion.md`
- `docs/decisions/0025-provider-timeouts-failure-classification.md`
- `docs/decisions/0026-idempotent-retry-boundaries.md`
- `docs/decisions/0027-validated-llama-cpp-release-updates.md`
- `docs/decisions/0028-explicit-provider-uninstall-cache-boundaries.md`
- `docs/decisions/0030-provider-generation-observability.md`
- `docs/decisions/0031-immutable-generation-snapshot.md`
- `docs/decisions/0032-immutable-generation-profile-core.md`
- `docs/decisions/0033-model-scoped-generation-profile-catalog.md`
- `docs/decisions/0034-conversation-generation-selection-and-profile-binding.md`
- `docs/decisions/0035-immutable-message-revisions-and-legacy-migration.md`
- `docs/decisions/0036-durable-generation-snapshot-provenance.md`
- `docs/decisions/0037-conversation-branch-graph.md`
- `docs/decisions/0038-revisioned-conversation-context-and-budget.md`
- `docs/decisions/0039-branch-scoped-generation-selection.md`
- `docs/decisions/0040-active-branch-generation-profile-selection-ui.md`
- `docs/decisions/0041-generation-profile-editor-working-draft.md`
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
