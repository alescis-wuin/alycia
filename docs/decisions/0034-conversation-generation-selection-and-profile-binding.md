# ADR 0034 - Conversation generation selection and profile binding

## Status

Accepted.

## Context

Lot 10B.1 introduced immutable per-generation state, Lot 10B.2 introduced reusable generation-profile behavior, and Lot 10B.3 introduced model-scoped profile catalogs with persistent drafts and immutable confirmed revisions.

The next boundary must make model/profile choice a conversation-level persistent input and resolve that choice at the instant a turn begins. The selected stable profile identity must not pin an old revision forever: a later confirmed profile update should be used by the next generation while the generation already in flight remains bound to the exact revision it resolved.

The current provider configuration is global and the llama.cpp runtime is started with one loaded model/context configuration. Existing conversations also predate profile selection. Introducing conversation selection must therefore avoid silent provider/model switching and must preserve the legacy generation path until a conversation has an explicit model/profile selection.

## Decision

Introduce a provider-neutral conversation-generation selection boundary in Application.

### Persisted conversation selection

`ConversationGenerationSelection` contains:

- the stable `ConversationId`;
- one `GenerationProfileModelScope` (`ProviderId + ModelReference`);
- one stable `GenerationProfileId`.

The selection deliberately stores the profile entity identifier rather than a revision identifier. It is therefore stable across profile edits. `IConversationGenerationSelectionStore` exposes model/profile selection load, save, and delete operations by conversation.

Infrastructure implements `JsonConversationGenerationSelectionStore` as a schema-versioned same-directory-temp-file JSON document. It preserves independent conversation entries, rejects duplicate or malformed stored selections, and restores the same selection after restart. Conversation JSON remains unchanged.

### Resolution at turn start

`ConversationGenerationResolver` resolves an explicit selection immediately before a response request is created:

1. load the persisted conversation selection;
2. require a saved provider configuration for the selected provider whose model reference still matches the selected model scope;
3. load the selected model's generation-profile catalog;
4. resolve the selected stable profile identifier;
5. for a custom profile, resolve its latest confirmed immutable revision at that instant;
6. build the immutable `GenerationSnapshot` from the selected provider/model, the provider configuration's runtime context size, and the selected profile's generation options;
7. expose the selected profile's base system instructions as resolved request instructions.

The built-in `Default` profile is represented by its real stable `GenerationProfileId` and `ProfileRevisionId = null`. No synthetic revision is created. Its empty profile generation options retain provider/model-default semantics.

For an explicitly selected profile, profile generation options are the request-level generation options. They are not merged with the legacy global provider sampling values because `null` in `InferenceGenerationOptions` already means provider/model default. The saved provider configuration continues to own the runtime-level model reference and context size. Existing conversations with no persisted conversation selection follow the unchanged legacy path, including the existing global generation settings.

### Generation snapshot binding

`GenerationSnapshot` gains optional `ProfileId` and `ProfileRevisionId` fields. A revision identifier is invalid without a profile identifier. A selected built-in default profile therefore has a profile identifier but no revision identifier; an explicitly selected custom profile is captured with both its stable profile identifier and the exact confirmed revision identifier resolved for the turn.

The turn use cases resolve once before invoking the responder. A later profile edit or conversation selection change cannot mutate the request already in flight.

This lot carries the resolved snapshot through `ConversationResponseRequest`; durable attachment to a future assistant-message revision remains a later Lot 10B boundary.

### Provider execution truthfulness

A request carrying a generation snapshot is routed by its captured provider identifier rather than by mutable global provider selection. The llama.cpp runtime validates that the captured model reference and runtime-level context size match the model that is actually loaded. It then uses the snapshot's generation options for that request. A mismatch is rejected before the generation POST instead of silently producing provenance for a different active model.

Resolved profile base system instructions are carried separately on `ConversationResponseRequest` and serialized as the leading system message by the llama.cpp chat adapter. Existing persisted conversation messages are not mutated and no synthetic Domain `ChatMessage` is created.

### Compatibility and UI boundary

An absent conversation selection is a valid legacy state. It is not auto-migrated to `Default`, because doing so would silently replace previously configured global sampling behavior with native provider/model defaults. Once future Presentation work explicitly saves a conversation model/profile choice, the new profile-bound path applies.

Lot 10B.4 introduces the persistence and execution contracts but does not add the dual model/profile selector UI, profile editing UI, message revision persistence, timeline events, context revisions, branching, or context-budget accounting.

## Consequences

- Model/profile choice has a durable provider-neutral conversation boundary without changing Domain conversation JSON.
- A conversation follows the latest confirmed revision of its selected custom profile on the next turn while every started turn keeps an exact immutable revision reference.
- The default profile remains revisionless instead of fabricating user history.
- Profile system instructions and sampling behavior can affect the real provider request without making Infrastructure understand profile catalogs.
- Per-conversation provider/model provenance cannot silently diverge from the loaded llama.cpp model/context configuration.
- Existing conversations remain behaviorally compatible until an explicit selection is persisted.
- Durable assistant-message provenance, message revisions, context provenance, branching, and explicit context budgeting remain subsequent Lot 10B work.
