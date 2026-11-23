using Alycia.Contracts.Documents;

namespace Alycia.Modules.Testing;

public sealed class SnapshotProjects(IEnumerable<ProjectIndexOwner> active) : IProjectIndexOwners
{
    private readonly ProjectIndexOwner[] _active = active.OrderBy(p => p.Id).ToArray();
    public Task<ProjectIndexOwner[]> ActivePageAsync(Guid? after, int count, CancellationToken ct)
    {
        if (count is < 1 or > 512) throw new ArgumentOutOfRangeException(nameof(count));
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(_active.Where(p => !after.HasValue || p.Id.CompareTo(after.Value) > 0).Take(count).ToArray());
    }
}
