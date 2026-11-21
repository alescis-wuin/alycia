# Contract checks

| Boundary | Fast characterization | Integrated evidence |
|---|---|---|
| Settings / transport | ConversationSettingsTransferTests reads a fixed export from 309d610, and checks strict shape, precision and limits. ConfigurationTransferTests exercises the typed client. | Configuration round trip and stale revision in the HTTP harness. |
| Conversations / streaming | StreamingTests covers partial failure and cancellation after a delta; replay is checked before a stale revision. | PostgresChatTests checks atomic writes and uniqueness; HTTP checks terminal acknowledgement. |
| Projects / access | LocalChatAccessTests, WorkspaceServicesTests | HTTP boreale verifies duplicate names, membership, isolated lookup, exclusion, concurrent profile update, revision and archives. |
| Retrieval / provenance | ProjectRetrievalTests, CitationAuditTests, RoutingAndRerankTests; deterministic context-v3 and v4 corpora | PersistentEmbeddingTests and project snapshot HTTP checks. |
| Index / ingestion | FileTests, StructuredDocumentsTests, CodeStructureTests, analysis checkpoints | KnowledgePipelineTests and PersistentEmbeddingTests cover interrupted processing, retries, stale jobs and scope. |
| Context / memory | RecursiveContextTests, StructuredMemoryTests, SlidingContextTests, AdaptiveBudgetTests | TemporalMemoryIntegrationTests; HTTP tool-summary, context-quality. |
| Tools / provider | OllamaChatModelTests, StreamingTests, ProjectMcpClientTests, ToolCallingTests | Loopback mock Ollama, real API and one disposable PostgreSQL database per HTTP run. |
| Presentation | Avalonia headless tests with mock HTTP; DraftStore and typed clients | The manual visual smoke remains distinct from headless assertions. |

Use the project-level filter `dotnet test tests/Alycia.Modules.Chat.Tests --filter 'FullyQualifiedName~StreamingTests'` for a focused change. Set `ALYCIA_CHAT_TEST_CONNECTION` only for an isolated administrator that can create test databases. Tests that create a fresh database clean it up in their own run. Use the published scripts for end-to-end checks.
