using Microsoft.EntityFrameworkCore;

namespace Alycia.Modules.Chat.Infrastructure;

public sealed class ProjectIndexOwners(ChatDbContext database) : IProjectIndexOwners
{
    public Task<ProjectIndexOwner[]> ActivePageAsync(Guid? after, int count, CancellationToken ct)
    {
        var query = database.WorkspaceProfiles.AsNoTracking().Where(c => !c.Archived);
        if (after.HasValue) query = query.Where(c => c.Id.CompareTo(after.Value) > 0);
        return query.OrderBy(c => c.Id).Take(count).Select(c => new ProjectIndexOwner(c.Id, c.Revision)).ToArrayAsync(ct);
    }
}
