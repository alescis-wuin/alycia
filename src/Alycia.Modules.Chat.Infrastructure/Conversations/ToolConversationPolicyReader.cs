using Microsoft.EntityFrameworkCore;

namespace Alycia.Modules.Chat.Infrastructure;

public sealed class ToolConversationPolicyReader(ChatDbContext database) : IToolConversationPolicy
{
    public Task<ToolConversationPolicy?> ReadAsync(Guid conversation, CancellationToken ct) =>
        database.Conversations.AsNoTracking().Where(c => c.Id == conversation)
            .Select(c => new ToolConversationPolicy(c.SettingsJson, c.MessageCount)).SingleOrDefaultAsync(ct);

    public Task<bool> HasHistoryAsync(Guid conversation, CancellationToken ct) =>
        database.Messages.AnyAsync(m => m.ConversationId == conversation, ct);
}
