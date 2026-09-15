# ADR 0005: Desktop host owns platform composition

## Status

Accepted.

## Context

The shared Avalonia presentation layer now needs real conversation lifecycle services while remaining independent from persistence implementations. The previous foundation rule allowed the desktop host to reference Presentation only, which was sufficient while the UI was static but prevents the executable from selecting the JSON repository and composing application use cases.

## Decision

`Alicia.Desktop` is the platform composition root.

The desktop host may reference `Alicia.Infrastructure` and `Alicia.Presentation` so it can:

- choose the platform-local conversation storage directory;
- create the local conversation runtime;
- inject application use cases into the shared `MainViewModel`;
- start the Avalonia application.

`Alicia.Presentation` continues to depend only on Application and Domain. It must not reference Infrastructure or the Desktop host.

The desktop host must not contain conversation rules or persistence logic. Its responsibility is composition and platform startup only.

## Consequences

The executable now has a deliberate outer-layer dependency on Infrastructure. This is acceptable because composition belongs at the application boundary, while the shared Presentation project remains reusable by future hosts with different infrastructure choices.

A future Android, iOS, or browser host can supply its own storage and composition without changing the shared ViewModels or views.
