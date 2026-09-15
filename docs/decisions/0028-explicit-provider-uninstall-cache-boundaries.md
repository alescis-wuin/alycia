# ADR 0028 — Explicit provider uninstall and cache boundaries

## Status

Accepted for root Lot 10.6.

## Context

Lot 10.5 keeps previous Alicia-managed llama.cpp releases so an update can be activated without destroying the prior runtime. The managed provider directory also contains a potentially large local model cache. Runtime binaries, retained releases, model cache, provider configuration, conversations and Presentation state have different ownership and recovery costs; treating them as one deletion target would make uninstall ambiguous and unnecessarily destructive.

## Decision

Application exposes an optional `IInferenceProviderMaintenanceRuntime` capability with provider-neutral storage inspection, retained-release cleanup, and an explicit `InferenceProviderRemovalMode` separating `RuntimeOnly` from `RuntimeAndModelCache`.

For the managed llama.cpp adapter, Alicia owns only these maintenance scopes below `providers/llama.cpp/`:

```text
runtime scope: installation.json, releases/, .staging/, logs/
model-cache scope: models/
```

`providers/configuration.json`, conversations, UI state, and the legacy provider settings file are outside those destructive scopes.

Every destructive action requires a separate Presentation confirmation. Requesting an action never performs deletion. `Escape`/Cancel dismiss the confirmation. Alicia does not stop a running provider implicitly; destructive maintenance is refused while the managed server is running.

Retained-release cleanup preserves the active managed release and removes only inactive release directories whose names match the managed `b<sequence>` release format. Unknown directories are preserved rather than guessed to be disposable.

Infrastructure canonicalizes every deletion target beneath the configured runtime root and never follows reparse-point/symbolic-link directories. Installation metadata is not trusted as a deletion path: the active release is recognized only when its version maps back to the expected Alicia-owned `releases/<version>/llama-server` path.

Cancellation is honored before destructive mutation begins. Once the user-confirmed deletion scope starts, Alicia completes that bounded scope rather than intentionally leaving a half-deleted runtime tree.

## Consequences

- runtime uninstall can preserve downloaded model data;
- full runtime + cache removal remains available but cannot happen accidentally;
- old releases retained by Lot 10.5 can be reclaimed independently;
- configuration and conversation history survive provider maintenance;
- no cleanup follows symlinks or metadata paths outside Alicia-owned storage;
- reinstall after runtime-only removal can reuse the existing local model cache.
