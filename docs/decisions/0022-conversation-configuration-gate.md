# ADR 0022 — Conversation configuration gate

## Status

Accepted — UIX-01 Stage 10B.

## Context

The Conversation workspace previously kept its composer visible when the local inference provider was unavailable. The text box could remain interactive while Send was disabled, and the user had to infer whether the next step was provider installation, provider inspection, model configuration, or model startup.

Alicia already owns explicit provider lifecycle and model-configuration workspaces. The Conversation workspace therefore needs a deterministic availability projection without duplicating provider or model editors.

## Decision

Presentation owns a `ConversationConfigurationGateViewModel` that projects the current provider/model state into one recommended action.

The mapping is deterministic:

| Condition | Gate action |
|---|---|
| no provider is available | Configure provider → Providers |
| selected provider has not been inspected | Check provider |
| provider is missing | Install provider |
| provider is unsupported or faulted | Review provider → Providers |
| provider is ready but no saved model exists | Configure model → Models |
| provider is ready but model settings are dirty | Review model settings → Models |
| provider and saved model are ready | Load model |
| provider is running | gate hidden |
| detect/install/start/stop is in progress | single disabled progress CTA |

`MainViewModel` executes provider actions directly where they are already safe and idempotent enough for the existing lifecycle contract. For configuration actions it raises an internal typed workspace-navigation request. `ShellViewModel` remains the owner of global workspace selection and handles that request.

When no conversations exist and local AI is unavailable, the gate becomes the central onboarding state and the empty history panel is hidden. When conversations exist, history and messages remain readable while the gate replaces only the composer.

## Consequences

- Conversation history remains usable independently of provider readiness.
- The composer is not presented as available when generation cannot start.
- Exactly one primary recovery action is visible at a time.
- Provider/model editing remains in the dedicated global workspaces.
- No Domain, Application, Infrastructure, persistence, or provider contract changes are required.
- Stage 10C can focus on responsive completion and the final delete modal without revisiting availability diagnosis.
