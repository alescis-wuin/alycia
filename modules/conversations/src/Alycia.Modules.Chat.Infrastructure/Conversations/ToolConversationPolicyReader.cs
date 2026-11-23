using Alycia.Modules.Chat.Domain;
using Alycia.Modules.Chat.Application;
using Microsoft.EntityFrameworkCore;

namespace Alycia.Modules.Chat.Infrastructure;

public sealed class ToolConversationPolicyReader(DbContext database, ChatOptions defaults) : IToolConversationPolicy
{
    public async Task<ToolConversationPolicy?> ReadAsync(Guid conversation, CancellationToken ct)
    {
        var snapshot = defaults.Copy();
        var owner = await database.Set<Conversation>().AsNoTracking().Where(c => c.Id == conversation)
            .Select(c => new { c.SettingsJson, c.MessageCount }).SingleOrDefaultAsync(ct);
        return owner is null ? null : ToolConversationPolicy.Capture(
            StoredConversationSettings.Read(owner.SettingsJson, () => snapshot), owner.MessageCount);
    }

    public Task<bool> HasHistoryAsync(Guid conversation, CancellationToken ct) =>
        database.Set<ChatMessage>().AnyAsync(m => m.ConversationId == conversation, ct);
}
