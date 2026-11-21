# Module ownership and decisions

## Dependency direction

The two bounded contexts are Chat and Workspaces. Keep the API as the composition root and the existing domain, application and infrastructure projects. Add one dependency-free transport project for the settings schema and shared response records. Avalonia must build with only that product reference. The database remains one PostgreSQL database. Domain objects never call a provider, file reader or DbContext.

| Owner | Inputs and outputs | Transaction or lifetime |
|---|---|---|
| Conversations | `IChatRepository.BeginTurnAsync`, `FindTurnAsync`, `AppendTurnAsync`; messages, request ID, revision | One per-owner gate and database snapshot until both messages and memory are saved. Replay precedes revision validation. |
| Workspaces / projects | Canonical workspace ID/name, profile, archive, membership and overrides | One connection crosses schemas when creating the workspace/profile. EF concurrency tokens protect revisions. Existing Workspaces API can create a workspace before its chat profile exists. |
| Documents | Stream, path, size limits → extracted text, status, file metadata and SHA | Original content is immutable. Batch and owner distinguish retries. Database rows and storage lifetime remain coordinated by the importer. |
| Index | Owner and settings revision → persisted jobs, vectors, knowledge units and progress | Background scopes; cancellation leaves resumable state. A stale revision cannot publish progress for a new job. |
| Retrieval | `IProjectRetrieval`, `IProjectIndex`, `IPassageReranker` → scored, scoped source evidence | Select only eligible attachments. The immutable index snapshot may be reused. No query changes workspace membership. |
| Context | Conversation, history, user text, token counter and settings → prepared window | Read through the caller's repository, including protected complete turns. Return pending memory changes without committing. |
| Memory | Ledger/temporal reads, selected evidence, pending update → conversation memory | Apply prepared changes inside the turn before the atomic append. Original messages and source hashes remain authoritative. |
| Inference | `IChatModel`, `ILocalEmbeddings`, `ITextTokenCounter` → replies, deltas, vectors, counts | Per-call cancellation; active inference deadline starts after acquisition. The global foreground reservation and per-conversation turn lease have different owners. |
| Tools | `IProjectTools`, `ISnapshotProjectTools` → bounded observations | Project-attached turns use their local transaction snapshot. Loopback MCP retains capabilities, deadlines and result limits. |
| Transport / API | Existing HTTP methods, revision and request DTOs; NDJSON `delta/done/error` | API owns HTTP, connection checks, error envelopes, DI, worker startup. No business SQL in routes. |
| Presentation | Typed HTTP client, dialogs, draft and stream state; immutable transport projections | Window lifetime owns cancellation and navigation. Presenters own transient states and have no EF or server-service dependency. |

## Decisions

1. **Shared transport.** A single `Alycia.Contracts` assembly removes the client’s transitive server dependency. Share versioned settings, diagnostics and wire records. Keep database entities and runtime provider options on the server. Client-only projections may read the existing JSON fields. Preserve property names, nullability, bytes of the reference v1 export, NDJSON framing and terminal acknowledgement.
2. **Data ownership.** `WorkspaceProfile` belongs logically to Projects; its existing Chat EF mapping remains. `Attachment` is document metadata with exactly one owner. `chat.workspace_file_overrides` records only conversation-specific choices. Scoped project operations on both schemas use the existing Chat connection and explicit transaction. No migration or additional DbContext is required.
3. **Settings and runtime state.** The wire schema owns persisted fields. The application owns request-local runtime values such as retrieved candidates, metering, tokenizer and progress. A new default object is obtained for each unconfigured conversation. Enrichment settings are an immutable record of primitive values and strings. Changing the v1 property set requires an explicit file migration.
4. **Access.** Keep middleware checking the real TCP peer before binding, Development gating and opt-in. Keep snapshot-based tool selection, admission and source integrity checks within the owner of each read. Do not move authorization into presentation.
5. **Extraction size.** Inference, documents, index, retrieval, context, memory and tools use service or namespace boundaries inside the existing Chat application/infrastructure assemblies. A new business assembly would currently require duplicating the same conversation and attachment contracts. Preserve the current deployment graph.
6. **Verification.** Pin exact bytes of an export from the source commit, exercise the real API with a simulated Ollama and dedicated database, and compare ordered benchmark rows on identical corpus hashes. Headless Avalonia tests exercise presentation independently. No external model call belongs to the deterministic suite.

## Change ownership

Feature contributors change only the implementation, tests and module documents listed in their task. The integrator owns the solution, project references, shared transport and options, registrations, migration history and route group. Propose contract changes before editing consumers. Use one worktree per independent task, merge against the frozen contract, then run a focused test of the integrated seam. Revert phase commits in reverse order if necessary.
