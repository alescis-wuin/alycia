# ADR-0001: Establish a host-separated Avalonia foundation

## Status

Accepted

## Context

Alicia must remain portable while keeping AI providers, storage, UI state, and platform bootstrapping independently testable.

## Decision

Use Clean Architecture boundaries with a shared Avalonia presentation project and a separate desktop executable host. Keep provider-specific code out of Domain, Application, and Presentation.

## Consequences

- Windows, Linux, and macOS can use the desktop host immediately.
- Additional Android, iOS, or browser hosts can reference the shared presentation layer later.
- Infrastructure adapters can evolve without forcing the ViewModels to depend on provider SDKs.
- A small amount of project structure exists before feature implementation, but dependency rules are machine-checked.
