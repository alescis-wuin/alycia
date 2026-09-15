# ADR-0002: Keep the conversation core provider-neutral

## Status

Accepted

## Context

Alicia will integrate several AI providers and local runtimes over time. Provider-specific request and response models must not become domain concepts because they would couple conversation behavior to external APIs and make offline testing harder.

## Decision

The first conversation core defines only provider-neutral concepts:

- typed conversation and message identifiers;
- system, user, and assistant message roles;
- immutable message data;
- a conversation aggregate responsible for message-order and identity invariants;
- an application-layer repository port;
- explicit use cases for conversation creation and message appending.

Provider adapters, persistence implementations, streaming completions, tool calls, attachments, and UI workflows remain outside this decision and will be added through later work packages.

## Consequences

- Domain and application tests run without network access or provider credentials.
- Infrastructure adapters can map external provider contracts to Alicia concepts without leaking those contracts inward.
- Presentation code can depend on stable application use cases while provider selection remains an infrastructure concern.
- Future message capabilities can extend the core deliberately instead of inheriting constraints from the first provider integration.
