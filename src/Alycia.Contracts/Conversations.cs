namespace Alycia.Contracts.Chat;

public sealed record ConversationSummary(Guid Id, string Title, string Model, string SystemPrompt,
    DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc, int Revision, int MessageCount,
    string ContextNotes = "", string SettingsJson = "{}", string MemoryText = "", int MemoryThroughSequence = 0,
    string MemorySourceHash = "", string MemoryModel = "", string MemoryLedgerJson = "{}", Guid? WorkspaceId = null);

public sealed record MessageResponse(Guid ConversationId, int Sequence, Guid RequestId, string Role,
    string Content, DateTimeOffset CreatedAtUtc, string DiagnosticsJson, string ToolTraceJson);

public sealed record TurnResponse(IReadOnlyList<MessageResponse> Messages, int? HistoryMessagesIncluded,
    bool Replayed, int FilesIncluded, int? EstimatedInputTokens, GenerationStats? Stats, GenerationUsage? Usage);
