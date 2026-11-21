using System.Data;
using Alycia.Contracts.Workspaces;
using Alycia.Platform.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Alycia.Modules.Chat.Infrastructure;

public sealed class LiveIndexFileAccess(DbContextOptions<ChatDbContext> options,
    Func<RelationalSession, IWorkspaceProfiles> profilesForSession, DocumentScope scope) : ILiveIndexFileAccess
{
    public async Task<ToolSourceEntry[]> CurrentEntriesAsync(Guid conversation, IReadOnlyCollection<Guid> candidates, CancellationToken ct)
    {
        var ids = candidates.ToArray();
        await using var current = new ChatDbContext(options);
        return await scope.ReadAsync(current, conversation, false, true, q => q.Where(f => ids.Contains(f.Id))
            .Select(f => new ToolSourceEntry(f.Id, f.Name, f.Size, f.ContextMode, f.Status, f.ContextMode != "excluded" && f.Text != "")).ToArrayAsync(ct), ct);
    }

    public async Task<bool> IsAllowedAsync(Guid owner, bool project, Guid file, CancellationToken ct)
    {
        await using var current = new ChatDbContext(options);
        await using var snapshot = await current.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
        if (project && await profilesForSession(new(current.Database)).FindAsync(owner, ct) is not { Archived: false }) return false;
        return await scope.ReadAsync(current, owner, project, true,
            q => q.AnyAsync(f => f.Id == file && f.ContextMode != "excluded", ct), ct);
    }
}
