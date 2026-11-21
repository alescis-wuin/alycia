using Microsoft.EntityFrameworkCore;

namespace Alycia.Modules.Chat.Infrastructure;

public sealed class DocumentMembershipOverrides(ChatDbContext database) : IDocumentMembershipOverrides
{
    public Task ClearAsync(Guid conversation, CancellationToken ct) =>
        database.WorkspaceFileOverrides.Where(o => o.ConversationId == conversation).ExecuteDeleteAsync(ct);
}
