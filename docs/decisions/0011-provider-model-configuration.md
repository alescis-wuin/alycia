# ADR 0011 — Explicit provider and model configuration

## Status

Accepted.

## Context

The managed llama.cpp CUDA adapter proves that Alicia can detect, install, start, stop, and stream from a real local provider. Lot 08 still binds the desktop directly to one runtime and stores only the last Hugging Face model reference in a llama.cpp-specific `settings.json` file.

Adding more providers or exposing inference controls from that shape would either leak provider-specific configuration into Presentation or introduce hidden defaults and implicit fallback behavior. Model/runtime options also have different lifetimes: context size affects server startup, while sampling values affect individual chat requests.

## Decision

Introduce provider-neutral configuration contracts in Application and keep their persistence/routing implementations in Infrastructure.

- Every provider has a stable identifier and display descriptor.
- `IInferenceProviderRegistry` enumerates registered providers, resolves their lifecycle runtime, and records an explicitly selected provider.
- The Infrastructure registry also implements the existing streaming responder port and refuses to stream when no provider has been explicitly selected. It never falls back to another provider automatically.
- `InferenceProviderConfiguration` stores a provider identifier, optional model reference, optional context size, and optional generation values.
- Empty optional values mean **provider/model default**. Alicia does not copy llama.cpp numeric defaults into its own configuration.
- Global provider configuration is stored separately from conversations in a versioned JSON document under Alicia's provider data directory.
- The selected provider and per-provider configuration are saved atomically. The configuration schema contains no credentials or access tokens.
- The Lot 08 llama.cpp `settings.json` model reference is read as a legacy migration source when the new global document does not yet exist. No sampling or context override is invented during migration.
- Presentation requires users to save changed settings before Start. An unsaved provider selection is not routed into conversation generation.
- Provider selection, model reference, context size, maximum output tokens, temperature, top-p, top-k, and seed are visible and validated in the UI. Optional values can remain blank.
- llama.cpp receives explicit context size at process start only when configured. Explicit generation values are serialized into `/v1/chat/completions`; unset values are omitted from the request.
- `HF_TOKEN` remains an environment-only secret owned by the provider process boundary and is not part of this configuration model.

The first implementation remains global rather than per-conversation. A future conversation-level override can compose on top of the same immutable Application configuration types without changing the current conversation JSON schema.

## Consequences

- Adding a second provider no longer requires replacing the MainViewModel provider contract.
- Provider routing becomes explicit and testable; an unavailable saved provider produces a configuration state instead of silently selecting another runtime.
- Existing Lot 08 users retain their last llama.cpp Hugging Face model reference through legacy migration.
- The UI exposes which values Alicia controls and which values remain provider defaults.
- Runtime-only settings that require restart cannot be changed while the provider is running.
- Generation settings are provider-neutral enough for the current local adapter but do not attempt to expose every llama.cpp sampler or backend option.
- Dependency injection remains manual in the Desktop composition root for now. A DI container is not introduced until composition complexity justifies the additional dependency.
