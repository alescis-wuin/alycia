namespace Alycia.Contracts.Chat;

public sealed record PreviewMessage(string Role, string Content);
public sealed record ContextWindowView(IReadOnlyList<PreviewMessage> Messages, int HistoryMessagesIncluded,
    int EstimatedInputTokens, int FilesIncluded = 0, bool NeedsSummary = false, int SummaryThroughSequence = 0,
    string SummaryText = "", int SummaryCalls = 0, IReadOnlyList<SourceExcerpt>? Sources = null,
    int FilesAvailable = 0, int ToolReserve = 0, RetrievalReport? Retrieval = null, MemoryReport? Memory = null);

public sealed record WorkspaceNoteView(Guid Id, string Text, Guid? OriginConversationId, int? OriginSequence, DateTimeOffset UpdatedAtUtc);
