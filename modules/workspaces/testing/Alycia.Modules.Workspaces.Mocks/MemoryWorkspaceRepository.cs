using Alycia.Modules.Workspaces.Application;
using Alycia.Modules.Workspaces.Domain;

namespace Alycia.Modules.Workspaces.Tests.Doubles;

public sealed class MemoryWorkspaceRepository : IWorkspaceRepository
{
    private readonly Dictionary<Guid, Workspace> _items = [];
    public int Count { get { lock (_items) return _items.Count; } }

    public Task<bool> ExistsByNameAsync(string name, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        lock (_items)
            return Task.FromResult(_items.Values.Any(w => string.Equals(w.Name, name, StringComparison.OrdinalIgnoreCase)));
    }
    public Task<Workspace?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        lock (_items) return Task.FromResult(_items.GetValueOrDefault(id));
    }
    public Task AddAsync(Workspace workspace, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        lock (_items)
        {
            if (_items.Values.Any(w => string.Equals(w.Name, workspace.Name, StringComparison.OrdinalIgnoreCase)))
                throw new WorkspaceNameConflictException();
            _items.Add(workspace.Id, workspace);
        }
        return Task.CompletedTask;
    }
}
