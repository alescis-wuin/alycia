# Functional baseline

All paths and counts in this inventory refer to `309d610425786e668782769f5b7650902d11d37b`.

## Branch selection

| Branch | Tip | Commit date | Commits behind | Commits exclusive |
|---|---|---|---:|---:|
| `develop` | `8179f9d` | 2026-11-05 23:43:11 +0100 | 49 | 0 |
| `feature/local-client-v1-20261116` | `228fde1` | 2026-11-17 08:29:40 +0100 | 27 | 0 |
| `feature/local-client-v4-context-quality-20261117` | `309d610` | 2026-11-21 15:57:37 +0100 | 0 | 0 |
| `main` | `b1777f6` | 2026-11-05 03:23:39 +0100 | 71 | 0 |
| `prototype/conversation-files` | `e1c5bc0` | 2026-11-06 09:42:52 +0100 | 44 | 0 |
| `prototype/local-ollama-chat` | `3e1c49f` | 2026-11-06 08:49:33 +0100 | 46 | 0 |
| `testing` | `b1777f6` | 2026-11-05 03:23:39 +0100 | 71 | 0 |

The v4 head contains the foundation, PostgreSQL, Ollama, file and v3 project work, plus persistent indexing, context diagnostics and isolated per-conversation settings. All six other branches are ancestors.

## Project references

| Project | Direct references |
|---|---|
| `Alycia.ContextBenchmarks` | `Alycia.Modules.Chat.Infrastructure` |
| `Alycia.Api` | `Alycia.Modules.Workspaces.Infrastructure`, `Alycia.Modules.Chat.Infrastructure` |
| `Alycia.Client.Avalonia` | `Alycia.Modules.Chat.Application` |
| `Alycia.Modules.Chat.Application` | `Alycia.Modules.Chat.Domain` |
| `Alycia.Modules.Chat.Domain` | — |
| `Alycia.Modules.Chat.Infrastructure` | `Alycia.Modules.Chat.Application` |
| `Alycia.Modules.Workspaces.Application` | `Alycia.Modules.Workspaces.Domain` |
| `Alycia.Modules.Workspaces.Domain` | — |
| `Alycia.Modules.Workspaces.Infrastructure` | `Alycia.Modules.Workspaces.Application` |
| `Alycia.Client.Avalonia.Tests` | `Alycia.Client.Avalonia` |
| `Alycia.Modules.Chat.Tests` | `Alycia.Modules.Chat.Infrastructure`, `Alycia.Api` |
| `Alycia.Modules.Workspaces.Tests` | `Alycia.Modules.Workspaces.Application` |

Eight product projects, three test projects in the solution. The benchmarks project builds separately. The client references Chat.Application, which brings in Chat.Domain. The API owns opt-in, access filters, registrations, JSON transport and the hosted worker.

## Routes and contracts

Paths below are relative to the indicated group. The real streaming endpoint returns newline-delimited JSON (`application/x-ndjson; charset=utf-8`), with `delta`, then `done` after commit, or `error` after response start. Replay returns the stored turn.

| Group | Handler | HTTP routes |
|---|---|---|
| `/api/chat` | `ChatEndpoints.Analysis.cs` | `GET /conversations/{id:guid}/inventory` · `GET /conversations/{id:guid}/analyses` · `GET /conversations/{id:guid}/analysis-runs` · `DELETE /conversations/{id:guid}/analysis-runs/{runId:guid}` · `GET /conversations/{id:guid}/analyses/{analysisId:guid}` · `POST /conversations/{id:guid}/analyses` · `PATCH /conversations/{id:guid}/analyses/{analysisId:guid}` · `DELETE /conversations/{id:guid}/analyses/{analysisId:guid}` |
| `/api/chat` | `ChatEndpoints.Configuration.cs` | `GET /import-settings` · `PUT /import-settings` · `GET /conversations/{id:guid}/settings` · `PUT /conversations/{id:guid}/settings` · `POST /conversations/{id:guid}/context` · `GET /conversations/{id:guid}/files` · `PATCH /conversations/{id:guid}/files/{fileId:guid}` · `GET /prompts` · `POST /prompts` · `DELETE /prompts/{id:guid}` · `PUT /prompts/{id:guid}` |
| `/api/chat` | `ChatEndpoints.History.cs` | `GET /search` · `GET /conversations/{id:guid}/export` · `POST /import` · `POST /conversations/{id:guid}/fork` |
| `/api/chat` | `ChatEndpoints.Index.cs` | `GET /indexing` · `GET /indexing/knowledge` · `GET /conversations/{id:guid}/index/knowledge/history` · `POST /indexing` · `POST /indexing/pause` · `GET /conversations/{id:guid}/index` |
| `/api/chat` | `ChatEndpoints.Mcp.cs` | `GET /conversations/{id:guid}/tools` |
| `/api/chat` | `ChatEndpoints.Memory.cs` | `POST /conversations/{id:guid}/memory/reset` · `GET /conversations/{id:guid}/memory/temporal` · `DELETE /conversations/{id:guid}/memory/temporal` · `POST /conversations/{id:guid}/context/prepare` |
| `/api/chat` | `ChatEndpoints.Projects.cs` | `GET /projects` · `POST /projects` · `GET /projects/{id:guid}` · `PUT /projects/{id:guid}` · `PUT /conversations/{id:guid}/project` · `POST /projects/{id:guid}/notes` · `PUT /projects/{id:guid}/notes/{note:guid}` · `DELETE /projects/{id:guid}/notes/{note:guid}` · `POST /conversations/{id:guid}/files/{fileId:guid}/share` · `GET /projects/{id:guid}/files` · `POST /projects/{id:guid}/files/{batch:guid}` · `PUT /projects/{id:guid}/files/{fileId:guid}` · `DELETE /projects/{id:guid}/files/{fileId:guid}` · `DELETE /conversations/{id:guid}/overrides/{fileId:guid}` · `GET /projects/{id:guid}/files/{fileId:guid}/preview` · `GET /conversations/{id:guid}/files/{fileId:guid}/preview` |
| `/api/chat` | `ChatEndpoints.Streaming.cs` | `POST /conversations/{id:guid}/stream` |
| `/api/chat` | `ChatEndpoints.cs` | `GET /models` · `GET /conversations` · `POST /conversations` · `GET /conversations/{id:guid}/messages` · `PATCH /conversations/{id:guid}` · `DELETE /conversations/{id:guid}` · `POST /conversations/{id:guid}/files/{batchId:guid}` · `GET /conversations/{id:guid}/files/{fileId:guid}` · `DELETE /conversations/{id:guid}/files/{fileId:guid}` · `POST /conversations/{id:guid}/messages` |
| `/api/workspaces` | `WorkspaceEndpoints.cs` | `POST /` · `GET /{id}` |

Application root exposes `/healthz`, plus OpenAPI and Scalar in Development. Contract declarations at this revision include:

- `ChatEndpoints.Analysis.cs` — AnalyzeRequest, SelectAnalysis
- `ChatEndpoints.Configuration.cs` — ConfigureConversation, PreviewContext, ConfigureFile, SavePrompt
- `ChatEndpoints.History.cs` — ForkConversation
- `ChatEndpoints.Index.cs` — QueueIndex
- `ChatEndpoints.Memory.cs` — MemoryRequest
- `ChatEndpoints.Projects.cs` — ProjectName, EditProject, ProjectMembership, SetProjectMode, NoteEdit, NewProject
- `ChatEndpoints.Streaming.cs` — StreamMessage
- `ChatEndpoints.cs` — CreateConversation, RenameConversation, SendMessage
- `WorkspaceEndpoints.cs` — CreateWorkspaceRequest, WorkspaceDto, WorkspaceEnvelope
- `ChatApiClient.Analysis.cs` — AnalysisItem, AnalysisCreated, AnalysisRunItem, AnalysisDetails
- `ChatApiClient.Features.cs` — SettingsReply, ContextPreview, PromptItem, ConversationSearch
- `ChatApiClient.Projects.cs` — ProjectChoice, ProjectSettings, SourcePreview
- `ChatApiClient.cs` — ConversationSummary, ChatMessageItem, MessagePage, ChatSendResult, FileItem, UploadFile, ApiError

## Persistence and configuration

| Owner | Data at baseline |
|---|---|
| Workspaces | `workspaces.workspaces` stores the canonical ID and name. |
| Project settings | `chat.workspace_profiles`, `chat.workspace_file_overrides`, conversation membership and project-owned attachments. Project creation uses one Chat connection and transaction across both schemas. |
| Conversations and memory | `chat.conversations` (including settings and ledger), `chat.messages`, `chat.prompt_profiles`, `chat.temporal_memory`. |
| Documents, analysis and knowledge | `chat.attachments`, `chat.document_analyses`, `chat.import_settings`, `chat.embedding_jobs`, `chat.embedding_vectors`, `chat.knowledge_units`. Immutable originals and analysis checkpoints live below the configured storage root. |
| EF | Each schema has its own `__EFMigrationsHistory`. Historical versions below are immutable. |

Migrations:

- `20261106012533_InitialChat`
- `20261106080209_ConversationAttachments`
- `20261116194633_LocalClientConfiguration`
- `20261116205445_RecursiveConversationMemory`
- `20261116210142_DocumentAnalysesAndSourceMetadata`
- `20261117071032_ProjectToolsAndPersistentSources`
- `20261117083500_GenerationDiagnostics`
- `20261117090828_StructuredMemoryLedger`
- `20261118011000_BorealProjects`
- `20261118200000_PersistentEmbeddings`
- `20261120010000_TemporalMemory`
- `20261120100000_KnowledgeIndex`
- `20261105211324_InitialWorkspaces`

Configuration files are `global.json`, `src/Alycia.Api/appsettings{,.Development}.json`, launch settings, environment and Compose files. User settings JSON uses web casing, model/provider runtime fields are excluded, and the strict v1 transfer is bounded to 256 KiB.

## Namespaces, application ports and registrations

- `Alycia.Api` — Alycia.Api.Chat, Alycia.Api.Workspaces.
- `Alycia.Client.Avalonia` — Alycia.Client.Avalonia.
- `Alycia.Modules.Chat.Application` — Alycia.Modules.Chat.Application. Ports: IChatModel, IChatRepository, IChatTurnScope, IIndexedEmbeddings, ILocalEmbeddings, IPassageReranker, IProjectIndex, IProjectRetrieval, IProjectTools, ISnapshotProjectTools, ITemporalMemory, ITextTokenCounter.
- `Alycia.Modules.Chat.Domain` — Alycia.Modules.Chat.Domain.
- `Alycia.Modules.Chat.Infrastructure` — Alycia.Modules.Chat.Infrastructure.
- `Alycia.Modules.Workspaces.Application` — Alycia.Modules.Workspaces.Application. Ports: IWorkspaceRepository.
- `Alycia.Modules.Workspaces.Domain` — Alycia.Modules.Workspaces.Domain.
- `Alycia.Modules.Workspaces.Infrastructure` — Alycia.Modules.Workspaces.Infrastructure.

Chat registrations by lifetime at the base revision:

- Scoped — IChatRepository, EfChatRepository, ChatService, ImportSettingsStore, FileImport, DocumentAnalysisService, ProjectSourceReader, IProjectIndex, ProjectIndexService, KnowledgeStore, KnowledgeBuilder, PostgresEmbeddingStore, TemporalMemoryStore, ITemporalMemory, BackgroundEmbeddingIndex, IIndexedEmbeddings, IProjectRetrieval, ProjectRetrievalService, ProjectToolService, ISnapshotProjectTools.
- Singleton — LexicalIndexCache, IPassageReranker, ChatOptions, LocalChatAccess, FileStorage.
- HostedService — EmbeddingWorker.
- HttpClient — ILocalEmbeddings, OllamaEmbeddings, IProjectTools, ProjectMcpClient, IChatModel, OllamaChatModel.
- DbContext — ChatDbContext.

## Behaviour and evidence

| Invariant | Evidence at this revision |
|---|---|
| Replay before revision check, two messages or none, concurrent same-owner rejection | ChatService, StreamingTests, PostgresChatTests |
| Private local access and actual peer | LocalChatAccessTests, WorkspaceEndpoints and root middleware |
| Strict JSON, full names, precision, no runtime properties, size ceiling | ConversationSettingsTransferTests and client ConfigurationTransferTests |
| Scoped candidate selection, canonical hashes and anchors | ProjectRetrievalTests, CitationAuditTests, FileTests |
| Independent inference wait and execution deadlines | OllamaInferenceGate, OllamaChatModelTests |
| Job continuation, obsolete revision ignored, history scope | PersistentEmbeddingTests and KnowledgePipelineTests |
| Project membership, shared notes, optimistic revisions | HTTP harness, verify_boreale |

Rollback on a review branch follows reverse dependency order. Use `git revert <phase-commit>` for a single-parent commit or `git revert -m 1 <phase-commit>` for a phase merge. Restoring the source checkout needs no database rollback.
