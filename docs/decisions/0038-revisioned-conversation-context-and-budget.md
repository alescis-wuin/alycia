# ADR 0038 - Revisioned conversation context provenance and explicit budget

## Status

Accepted.

## Context

Lots 10B.5a–10B.5c provide immutable message revisions, durable generation snapshots, and a non-destructive conversation branch graph. A generation can therefore identify the exact message revisions and profile revision that produced an Assistant response, but persistent conversation context is not yet revisioned or represented in provenance.

The UI/UX model requires confirmed persistent context changes to be immutable and reconstructible, and requires a branch to inherit the state that was effective at its exact divergence point. Context must then be able to diverge independently on descendant branches. The generation pipeline also needs an explicit, provider-neutral context-budget contract without pretending that Alicia already knows provider-specific tokenization or effective defaults.

This lot must remain below Presentation and must not introduce RAG retrieval, files/assets, tools, MCP, multimodal payloads, or provider-specific tokenizer accounting.

## Decision

### One stable context entity per conversation

Each conversation exposes a stable `ConversationContextId` derived from its stable `ConversationId`. Confirmed context states are represented by immutable `ConversationContextRevision` values with:

- a `ConversationContextRevisionId` UUID;
- an optional parent revision;
- optional persistent instructions;
- an explicit `ReplaceProfileInstructions` flag;
- a confirmation timestamp;
- a deterministic SHA-256 payload hash over the self-contained context payload.

A null instruction payload is meaningful: with `ReplaceProfileInstructions = false` it removes conversation-level instructions while retaining profile instructions; with replacement enabled it explicitly clears the profile instruction contribution.

### Branch timeline bindings and exact divergence inheritance

Context revisions are stored once in the conversation-level revision registry. Each revision is owned by exactly one branch-local `ConversationContextBinding`, which records the message revision after which that context state became effective. A null binding anchor is permitted only on the root branch before its first message.

A child branch stores an optional `InheritedContextRevisionId`. That identifier is not the parent's current context at edit time: it is the exact context revision that was effective immediately before the first parent message omitted by the fork. Parent context changes confirmed later therefore cannot retroactively alter an existing child branch.

Context revisions created on a child chain from the context currently effective on that child. Branch activation changes only which branch state is projected; it does not rewrite context history.

### Generation binding and stale-turn protection

`ConversationTurnSnapshot` captures the complete context-revision registry plus branch context inheritance/bindings. Any context change while a provider response is in flight makes the turn stale just like a message or branch-graph mutation.

`ConversationGenerationResolver` becomes context-aware while retaining the legacy resolver interface for source compatibility. A revisioned active context may never be silently ignored by a legacy resolver.

Resolved system instructions follow two explicit modes:

- extend: profile instructions, then conversation-context instructions;
- replace: conversation-context instructions only.

The exact active `ConversationContextRevisionId` is written into the generation snapshot.

### Explicit context budget without fabricated token counts

`GenerationContextBudget` records only limits that are explicitly known from provider-neutral configuration:

- configured context-window tokens;
- configured reserved output tokens;
- maximum input tokens, derived only when both values are known.

If either configured value is absent, the dependent result remains null rather than inventing a provider/model default. A configured output reservation that consumes the entire configured context window is rejected before provider dispatch.

This budget is a limit/provenance contract, not measured prompt-token usage. Provider-specific tokenization and retrieval allocation remain future work.

### Persistence compatibility

Conversation JSON advances to schema v6 with `contextRevisions`, `inheritedContextRevisionId`, and branch-local `contextBindings`. Schemas 0, 2, 3, 4, and 5 remain readable; legacy documents contain no invented context and materialize v6 only on a later explicit save.

Generation-snapshot JSON advances to schema v2 for context revision provenance and explicit budget. Schema v1 remains readable with its original payload-hash contract. New resolver-produced snapshots use the v2 hash contract; existing v1 snapshots are not reinterpreted as if they had captured context provenance or a budget.

## Consequences

- confirmed persistent context is immutable, hash-verifiable, and branch-aware;
- editing old history inherits context from the actual divergence boundary rather than current parent state;
- parent and child context can evolve independently without rewriting history;
- generation snapshots identify the exact context revision used;
- stale context changes cannot be committed against an in-flight response;
- explicit limits are represented without fabricating token usage or provider defaults;
- RAG, files/assets, links/retrieval, tools, multimodal payloads, visual context editors, and provider-specific tokenizers remain outside 10B.6.
