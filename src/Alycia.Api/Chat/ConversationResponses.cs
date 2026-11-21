using Alycia.Modules.Chat.Application;
using Alycia.Modules.Chat.Domain;

namespace Alycia.Api.Chat;

internal static class ConversationResponses
{
    public static ConversationSummary ToResponse(this Conversation c) => new(c.Id, c.Title, c.Model, c.SystemPrompt,
        c.CreatedAtUtc, c.UpdatedAtUtc, c.Revision, c.MessageCount, c.ContextNotes, c.SettingsJson, c.MemoryText,
        c.MemoryThroughSequence, c.MemorySourceHash, c.MemoryModel, c.MemoryLedgerJson, c.WorkspaceId);

    public static MessageResponse ToResponse(this ChatMessage m) => new(m.ConversationId, m.Sequence, m.RequestId,
        m.Role, m.Content, m.CreatedAtUtc, m.DiagnosticsJson, m.ToolTraceJson);

    public static TurnResponse ToResponse(this SendMessageResult r) => new(r.Messages.Select(m => m.ToResponse()).ToArray(),
        r.HistoryMessagesIncluded, r.Replayed, r.FilesIncluded, r.EstimatedInputTokens, r.Stats, r.Usage);
}
