# Architecture overview

## Goals

Alicia separates conversation rules, application orchestration, external AI/provider integrations, shared Avalonia presentation, and executable platform hosts.

## Layers

### Domain

Framework-independent domain concepts and invariants. The conversation core owns typed conversation/message identifiers, normalized titles, creation/activity metadata, message roles, immutable message data, and aggregate-level lifecycle invariants.

### Application

Use cases and ports for response generation, persistence, clocks, files, search, tools, and other external capabilities. The conversation application slice exposes creation, loading, summary listing, renaming, message appending, deletion, a provider-neutral `IConversationResponder` response port, a response-generation use case, complete non-streaming turn orchestration, and an `IConversationRepository` persistence port without selecting an infrastructure implementation. Turn completion is tied to the triggering user-message identifier and rejects a generated response when the conversation snapshot changed before persistence.

### Infrastructure

Implementations for provider APIs, persistence, operating-system services, networking, and other external boundaries. The local conversation slice provides versioned JSON document persistence, legacy document reads, recent-activity summary listing, deletion, detached aggregate reconstruction, and an explicit composition object for the lifecycle use cases. A deterministic `DevelopmentConversationResponder` currently implements the Application response port so the desktop can exercise cancellation and retry without a provider SDK. Future external response adapters implement the same port here rather than leaking provider types into higher layers.

### Presentation

Shared Avalonia views and ViewModels for conversation history, selection, lifecycle actions, local user-message composition, message projection, empty/loading states, accessibility metadata, and user-facing error projection. Presentation owns transient draft/focus/scroll behavior, persists the user message before starting response generation, exposes cancellable generation plus retry for the existing trigger, and does not select persistence or responder implementations.

### Desktop

The desktop executable composition root for Windows, Linux, and macOS. It selects the local JSON conversation runtime, chooses the platform-local data directory, wires the deterministic development responder into `CompleteConversationTurnUseCase`, injects the resulting use cases into Presentation, and starts Avalonia. Other hosts can make different infrastructure choices while reusing `Alicia.Presentation`.

## Dependency rules

- Domain has no internal project dependency.
- Application may depend on Domain only.
- Infrastructure may depend on Application and Domain.
- Presentation may depend on Application and Domain, but never Infrastructure.
- Desktop is the platform-specific composition root and may depend on Presentation and Infrastructure; it must not contain domain rules or persistence behavior.
