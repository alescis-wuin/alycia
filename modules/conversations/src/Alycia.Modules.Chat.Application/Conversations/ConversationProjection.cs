using Alycia.Modules.Chat.Domain;

namespace Alycia.Modules.Chat.Application;

public static class ConversationProjection
{
    public static ConversationSummary Summary(Conversation c) => new(c.Id, c.Title, c.Model, c.SystemPrompt,
        c.CreatedAtUtc, c.UpdatedAtUtc, c.Revision, c.MessageCount, c.ContextNotes, c.SettingsJson, c.MemoryText,
        c.MemoryThroughSequence, c.MemorySourceHash, c.MemoryModel, c.MemoryLedgerJson, c.WorkspaceId);
}
