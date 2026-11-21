using System.Data;
using Alycia.Contracts.Workspaces;
using Alycia.Platform.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Alycia.Modules.Chat.Infrastructure;

public sealed class LiveIndexFileAccess(DbContextOptions<ChatDbContext> options,
    Func<RelationalSession, IWorkspaceProfiles> profilesForSession) : ILiveIndexFileAccess
{
    public async Task<ToolSourceEntry[]> CurrentEntriesAsync(Guid conversation, IReadOnlyCollection<Guid> candidates, CancellationToken ct)
    {
        var ids = candidates.ToArray();
        await using var current = new ChatDbContext(options);
        return await current.ModelSources(conversation).Where(f => ids.Contains(f.Id))
            .Select(f => new ToolSourceEntry(f.Id, f.Name, f.Size, f.ContextMode, f.Status, f.ContextMode != "excluded" && f.Text != "")).ToArrayAsync(ct);
    }

    public async Task<bool> IsAllowedAsync(Guid owner, bool project, Guid file, CancellationToken ct)
    {
        await using var current = new ChatDbContext(options);
        await using var snapshot = await current.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
        if (project && await profilesForSession(new(current.Database)).FindAsync(owner, ct) is not { Archived: false }) return false;
        var files = project ? current.Attachments.AsNoTracking().Where(f => f.WorkspaceId == owner) : current.ModelSources(owner);
        return await files.AnyAsync(f => f.Id == file && f.ContextMode != "excluded", ct);
    }
}
