using Microsoft.EntityFrameworkCore;

namespace Alycia.Modules.Documents.Infrastructure;

public sealed class DocumentMembershipOverrides(DbContext database) : IDocumentMembershipOverrides
{
    public Task ClearAsync(Guid conversation, CancellationToken ct) =>
        database.Set<WorkspaceFileOverride>().Where(o => o.ConversationId == conversation).ExecuteDeleteAsync(ct);
}
