# Architecture overview

## Goals

Alicia separates conversation rules, application orchestration, external AI/provider integrations, shared Avalonia presentation, and executable platform hosts.

## Layers

### Domain

Framework-independent domain concepts and invariants. The conversation core owns typed conversation/message identifiers, normalized titles, creation/activity metadata, message roles, immutable message data, and aggregate-level lifecycle invariants.

### Application

Use cases and ports for response generation, provider lifecycle management, persistence, clocks, files, search, tools, and other external capabilities. The conversation application slice exposes creation, loading, summary listing, renaming, message appending, deletion, the provider-neutral `IConversationResponder` and `IStreamingConversationResponder` response ports, non-streaming generation/completion, streamed turn orchestration, and an `IConversationRepository` persistence port without selecting an infrastructure implementation. `IInferenceProviderRuntime` exposes provider detect/install/start/stop state plus a provider-neutral installation-progress contract without referencing llama.cpp, CUDA, processes, or HTTP. Application also owns immutable provider descriptors/configuration, generation options, an explicit provider-registry port, and a configuration-store port. Both turn paths are tied to the triggering user-message identifier and share snapshot validation. Streaming accumulates validated content deltas in Application, revalidates history only after the stream completes, and persists exactly one final immutable Assistant message.

### Infrastructure

Implementations for provider APIs, persistence, operating-system services, networking, and other external boundaries. The local conversation slice provides versioned JSON document persistence, legacy document reads, recent-activity summary listing, deletion, detached aggregate reconstruction, and an explicit composition object for the lifecycle use cases. The provider slice implements an explicit `InferenceProviderRegistry`, a versioned atomic JSON configuration store, and the llama.cpp adapter. The registry maps stable provider identifiers to lifecycle runtimes and streaming responders and refuses implicit fallback. The llama.cpp slice detects CUDA-capable executables, builds the managed Linux x64 `llama-server` target from the official source release, reports installation progress, launches `llama-server -hf` on loopback with an optional configured context size, owns its process lifecycle/model cache, waits for health readiness, and maps OpenAI-compatible SSE responses into Application chunks. Explicit request sampling options are serialized only when configured; provider-specific process/HTTP/CUDA details remain confined to Infrastructure.

### Presentation

Shared Avalonia views and ViewModels for conversation history, selection, lifecycle actions, explicit provider/model settings, provider lifecycle controls and phase-aware installation progress, local user-message composition, message projection, empty/loading states, accessibility metadata, and user-facing error projection. `ShellViewModel` owns global Conversations/Providers/Models workspace selection plus one shared `MainViewModel`. `ShellView` is the Presentation root and renders a persistent 56 px icon-only global navigation rail with a delayed overlay label flyout. `MainView` is Conversation-only: its history surface supports title-first/message-content search, lazy three-message hover previews, overflow/right-click Rename/Delete actions, and a transient collapse/reopen state while provider and model controls remain exclusive to their dedicated global workspaces. UIX-01 stage 6 removes hard rail/history separators, makes conversation cards full-width interaction targets, centers and reorganizes history controls, keeps search and thread surfaces visually transparent, and makes the opaque composer shell the primary interaction surface with click-to-focus behavior and an icon-only send action. Presentation owns transient draft/focus/scroll behavior, persists the user message before starting response generation, projects a mutable Assistant-only streaming view model while deltas arrive, exposes Stop plus retry for the existing trigger, removes partial projections on cancellation/failure, and reloads the persisted immutable message after successful completion. It does not select persistence or responder implementations.

### Desktop

The desktop executable composition root for Windows, Linux, and macOS. It selects the local JSON conversation runtime, chooses platform-local data directories, creates the managed `LlamaCppProviderRuntime`, registers it under a stable provider identifier, creates the global JSON configuration store with Lot 08 legacy-model migration, wires the registry into `StreamConversationTurnUseCase`, constructs the shared `MainViewModel`, wraps it in the Presentation `ShellViewModel`, injects that shell through the Presentation factory, disposes the managed server at shutdown, and starts Avalonia. Other hosts can make different infrastructure choices while reusing `Alicia.Presentation`.

## Dependency rules

- Domain has no internal project dependency.
- Application may depend on Domain only.
- Infrastructure may depend on Application and Domain.
- Presentation may depend on Application and Domain, but never Infrastructure.
- Desktop is the platform-specific composition root and may depend on Presentation and Infrastructure; it must not contain domain rules or persistence behavior.
