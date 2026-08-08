# Architecture overview

## Goals

Alicia separates conversation rules, application orchestration, external AI/provider integrations, shared Avalonia presentation, and executable platform hosts.

## Layers

### Domain

Framework-independent domain concepts and invariants. The conversation core currently owns typed conversation/message identifiers, message roles, immutable message data, and aggregate-level message invariants.

### Application

Use cases and ports for AI completion, persistence, clocks, files, search, tools, and other external capabilities. The first application slice exposes conversation creation, message appending, and an `IConversationRepository` persistence port without selecting an infrastructure implementation.

### Infrastructure

Implementations for provider APIs, persistence, operating-system services, networking, and other external boundaries. The local conversation slice currently provides JSON document persistence with detached aggregate reconstruction and an explicit composition object for the existing use cases.

### Presentation

Shared Avalonia views, ViewModels, presentation state, navigation, localization, accessibility metadata, and user-facing error projection.

### Desktop

The desktop executable composition root for Windows, Linux, and macOS. Other hosts can be added later while reusing `Alicia.Presentation`.

## Dependency rules

- Domain has no internal project dependency.
- Application may depend on Domain only.
- Infrastructure may depend on Application and Domain.
- Presentation may depend on Application and Domain, but never Infrastructure.
- Desktop may depend on Presentation and is the platform-specific composition host.
