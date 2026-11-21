using System.Data;
using Alycia.Contracts.Workspaces;
using Alycia.Platform.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Alycia.Modules.Chat.Infrastructure;

public sealed class LiveIndexFileAccess(DbContextOptions<ChatDbContext> options,
    Func<RelationalSession, IWorkspaceProfiles> profilesForSession) : ILiveIndexFileAccess
{
    public async Task<bool> IsAllowedAsync(Guid owner, bool project, Guid file, CancellationToken ct)
    {
        await using var current = new ChatDbContext(options);
        await using var snapshot = await current.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
        if (project && await profilesForSession(new(current.Database)).FindAsync(owner, ct) is not { Archived: false }) return false;
        var files = project ? current.Attachments.AsNoTracking().Where(f => f.WorkspaceId == owner) : current.ModelSources(owner);
        return await files.AnyAsync(f => f.Id == file && f.ContextMode != "excluded", ct);
    }
}
