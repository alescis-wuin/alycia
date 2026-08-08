# Architecture overview

## Goals

Alicia separates conversation rules, application orchestration, external AI/provider integrations, shared Avalonia presentation, and executable platform hosts.

## Layers

### Domain

Framework-independent domain concepts and invariants.

### Application

Use cases and ports for AI completion, persistence, clocks, files, search, tools, and other external capabilities.

### Infrastructure

Implementations for provider APIs, persistence, operating-system services, networking, and other external boundaries.

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
