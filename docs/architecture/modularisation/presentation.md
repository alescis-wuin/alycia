# Boréale presentation map

Source `309d610`, 13 C# window parts and 89 named AXAML controls, including names inside templates. The table lists direct references in each source file. A named control inside a data template has its own name scope.

| Window part | Shared fields mentioned | Named controls looked up | Typed HTTP calls |
|---|---|---|---|
| `ChatWindow.Analysis.cs` | _client, _current, _generation, _lifetime | MessageInput | AnalysesAsync, AnalysisAsync, AnalysisRunsAsync, AnalyzeAsync, DeleteAnalysisAsync, DownloadAnalysisAsync, FilesAsync, InventoryAsync, PrepareMemoryAsync, PreviewAsync, ResetMemoryAsync, SelectAnalysisAsync, SettingsAsync |
| `ChatWindow.ConfigurationTransfer.cs` | _client, _current, _lifetime |  | ExportConfigurationAsync, ImportConfigurationAsync |
| `ChatWindow.Documents.cs` | _client, _current, _indexingOwner, _indexingProject, _lifetime, _project |  | ConfigureFileAsync, FilesAsync, PauseIndexingAsync, ProjectAsync, ProjectFilesAsync, RemoveProjectFileAsync, RestoreFileModeAsync, SetProjectFileModeAsync, SettingsAsync, ShareFileAsync, SourceAsync |
| `ChatWindow.Features.cs` | _client, _current, _lifetime, _project, _searchQuery | ActiveModelButton, AnalysisButton, ContextButton, ConversationList, ExportButton, ExportConfigurationButton, FilesButton, ForkButton, ImportConfigurationButton, ImportConversationButton, MessageInput, MoreConversationsButton, ProjectToolsButton, PromptsButton, RegenerateButton, SearchButton, SearchInput, SettingsButton | ConfigureAsync, ExportAsync, ForkAsync, ImportConversationAsync, MessagesAsync, ModelsAsync, ProjectAsync, PromptsAsync, SearchAsync, SettingsAsync |
| `ChatWindow.Files.cs` | _busy, _client, _current, _lifetime, _project | AttachArchiveCommand, AttachFileCommand, AttachProjectCommand, AttachmentActions, FolderButton, UploadButton | DeleteFileAsync, DownloadAsync, ImportLimitsAsync, UploadAsync |
| `ChatWindow.ImportSettings.cs` | _client, _lifetime |  | ImportLimitsAsync, SaveImportLimitsAsync |
| `ChatWindow.Indexing.cs` | _checkingIndex, _client, _current, _indexingOwner, _indexingProject, _indexingTimer, _lifetime, _project | IndexingProgressBar, IndexingStatusText | IndexingAsync, QueueIndexingAsync |
| `ChatWindow.Inspector.cs` | _client, _current, _evidence, _lifetime, _messages | InspectorBody, MessageInput | DeleteNoteAsync, PreviewAsync, ProjectAsync, SaveNoteAsync, SettingsAsync, SourceAsync |
| `ChatWindow.Knowledge.cs` | _client, _enabled, _lifetime, _model, _numbers |  | KnowledgeAsync, KnowledgeHistoryAsync, MessagesAsync |
| `ChatWindow.Navigation.cs` | _busy, _choosingProject, _client, _concentrating, _current, _evidence, _generation, _indexingOwner, _indexingProject, _inspectorOrigin, _lifetime, _messages, _project, _projects, _sidebarBeforeFocus, _sidebarManual | ActionMenu, ActiveModelButton, AddProjectButton, ApiUrlInput, AppearanceButton, BackButton, ChatPage, CloseInspector, ContextButton, ContextTab, ConversationList, FocusButton, ImportLimitsButton, Inspector, InspectorBody, InstructionsButton, MainContent, MemoryTab, MessageInput, ModelSelector, MoreButton, MoveButton, PageBody, PageScroll, PendingUserPanel, PreferencesPage, ProjectHeading, ProjectSelector, SaveAppearanceButton, SearchInput, Sidebar, SizeChoice, SourcesTab, StreamingPanel, ThemeChoice, TitleInput, ViewHeading, WrapChoice | MoveAsync, NewProjectAsync, ProjectAsync, ProjectsAsync, SaveProjectAsync |
| `ChatWindow.Prompts.cs` | _client, _lifetime | SystemPromptInput | DeletePromptAsync, PromptsAsync, SavePromptAsync |
| `ChatWindow.Tools.cs` | _client, _current, _lifetime |  | FilesAsync, McpEndpoint, ProjectToolsAsync |
| `ChatWindow.axaml.cs` | _busy, _client, _current, _draftTimer, _drafts, _evidence, _followOutput, _generation, _indexingOwner, _indexingProject, _lifetime, _loadingDraft, _messages, _pendingId, _project, _searchQuery, _sidebarManual | ActionMenu, ActiveModelButton, AttachProjectCommand, AttachmentActions, BusyIndicator, CancelButton, ContextButton, ConversationDetails, ConversationList, ConversationTools, CreationPanel, DeleteButton, DiagnosticsButton, EmptyHint, EmptyState, FeedbackText, FilesButton, FolderButton, ImportConversationButton, Inspector, InspectorFeedback, InstructionsButton, LatestButton, MessageInput, MessageList, MessageScroll, ModelSelector, MoreButton, MoreConversationsButton, NewButton, NewTitleInput, OlderButton, PendingUserLabel, PendingUserPanel, PendingUserText, PromptsButton, RefreshButton, RegenerateButton, RenameButton, SearchButton, SearchInput, SendButton, Sidebar, SidebarToggle, StreamingPanel, StreamingStatus, StreamingText, StreamingToolText, StreamingTools, SystemPromptInput, TitleInput, UploadButton, ViewHeading | CreateAsync, DeleteAsync, MessagesAsync, ModelsAsync, ProjectAsync, RenameAsync, SearchAsync, SettingsAsync, StreamMessageAsync |

## Declared view events

| Control (type) | AXAML event and handler |
|---|---|
| Button · Copier le message | Click → CopyMessageClick |
| Button · Code | Click → CopyCodeClick |
| MarkdownView · template child | CitationRequested → CitationClick |
| Button · Sources et détails | Click → EvidenceClick |
| Button · Télécharger | Click → DownloadFileClick |
| Button · Retirer | Click → RemoveFileClick |

## Named controls

`ActionMenu`, `ActiveModelButton`, `AddProjectButton`, `AnalysisButton`, `ApiUrlInput`, `AppearanceButton`, `AttachArchiveCommand`, `AttachFileCommand`, `AttachProjectCommand`, `AttachmentActions`, `BackButton`, `BusyIndicator`, `CancelButton`, `ChatPage`, `CloseInspector`, `ContextButton`, `ContextTab`, `ConversationDetails`, `ConversationHeader`, `ConversationList`, `ConversationTools`, `CreationPanel`, `DeleteButton`, `DiagnosticsButton`, `EmptyHint`, `EmptyState`, `ExportButton`, `ExportConfigurationButton`, `FeedbackText`, `FilesButton`, `FocusButton`, `FolderButton`, `ForkButton`, `ImportConfigurationButton`, `ImportConversationButton`, `ImportLimitsButton`, `IndexingProgressBar`, `IndexingStatusText`, `Inspector`, `InspectorBody`, `InspectorFeedback`, `InstructionsButton`, `LatestButton`, `MainContent`, `MemoryTab`, `MessageInput`, `MessageList`, `MessageScroll`, `ModelSelector`, `MoreButton`, `MoreConversationsButton`, `MoveButton`, `NewButton`, `NewTitleInput`, `OlderButton`, `PageBody`, `PageScroll`, `PendingUserLabel`, `PendingUserPanel`, `PendingUserText`, `PreferencesPage`, `ProjectHeading`, `ProjectSelector`, `ProjectToolsButton`, `PromptsButton`, `RefreshButton`, `RegenerateButton`, `RenameButton`, `SaveAppearanceButton`, `SearchButton`, `SearchInput`, `SendButton`, `SettingsButton`, `ShellGrid`, `Sidebar`, `SidebarToggle`, `SizeChoice`, `SourcesTab`, `StreamingPanel`, `StreamingStatus`, `StreamingText`, `StreamingToolText`, `StreamingTools`, `SystemPromptInput`, `ThemeChoice`, `TitleInput`, `UploadButton`, `ViewHeading`, `WrapChoice`

The window owns application lifetime, selection, navigation and UI-thread controls. Programmatic wiring in the C# parts supplements the AXAML events above. Conversation state must survive navigation, and upload, indexing and streaming responses must remain associated with their owner.

## Components after extraction

| Work area | Edit these components | Window integration |
|---|---|---|
| Conversation and navigation | ConversationComposer, ConversationTimeline, ProjectNavigation | The window binds controls, routes keyboard shortcuts and restores focus. These components hold pending request/draft identity, timeline ordering/pagination and project selection. |
| Documents | DocumentList, DocumentUpload | Dialogs pass the selected owner and files. Multi-file actions use confirmed revisions; uploads own the streams they opened. |
| Knowledge and indexing | KnowledgeBrowser, IndexingMonitor | The window renders the selected source and progress. The monitor owns polling, cancellation and the active owner. |
| Configuration | ConversationSettingsEditor, KnowledgeFields, PromptPresets, ChatApiClient.Configuration | The editor owns the controls, validation and submission of a conversation’s settings. The window opens the modal and reloads the confirmed revision. |
| Diagnostics and tools | EvidenceInspector, ToolInspection, ToolDisplay | The inspector resolves and renders evidence for its conversation. Tool inspection and trace formatting are independent of the ChatWindow class. |

The data template for conversation titles binds the same fields to the shared ConversationSummary wire type. The named controls, shortcuts, styles, labels and assets from the reference are retained. Run the client tests with `dotnet test tests/Alycia.Client.Avalonia.Tests`. For a desktop smoke check, launch `make demo-client`, select a conversation, press Ctrl+Shift+F twice to hide/restore the sidebar, and open settings. Cancel first, then confirm a change on a disposable conversation. Check the original draft and selected conversation when returning.
