using Microsoft.EntityFrameworkCore;

namespace Alycia.Modules.Chat.Infrastructure;

public sealed class ConversationIndexOwners(ChatDbContext database) : IConversationIndexOwners
{
    public Task<ConversationIndexOwner[]> PageAsync(Guid? after, int count, CancellationToken ct)
    {
        var query = database.Conversations.AsNoTracking();
        if (after.HasValue) query = query.Where(c => c.Id.CompareTo(after.Value) > 0);
        return query.OrderBy(c => c.Id).Take(count).Select(c => new ConversationIndexOwner(c.Id, c.WorkspaceId, c.Revision, c.SettingsJson)).ToArrayAsync(ct);
    }
}
