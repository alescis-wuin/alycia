using Alycia.Contracts.Workspaces;

namespace Alycia.Modules.Testing;

public sealed class MemoryWorkspaceDirectory : IWorkspaceIdentityDirectory
{
    private readonly Dictionary<string, WorkspaceIdentity> _names = new(StringComparer.OrdinalIgnoreCase);

    public Task<IReadOnlyList<WorkspaceIdentity>> ListAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        lock (_names)
            return Task.FromResult<IReadOnlyList<WorkspaceIdentity>>(_names.Values.OrderBy(w => w.Name, StringComparer.OrdinalIgnoreCase).ToArray());
    }

    public Task<bool> ExistsAsync(Guid id, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        lock (_names) return Task.FromResult(_names.Values.Any(w => w.Id == id));
    }

    public Task<WorkspaceIdentity> CreateAsync(string name, CancellationToken ct)
    {
        var normalized = name?.Trim();
        if (string.IsNullOrEmpty(normalized) || normalized.Length > 100) throw new ArgumentException("Nom de projet invalide.", nameof(name));
        ct.ThrowIfCancellationRequested();
        lock (_names)
        {
            var workspace = new WorkspaceIdentity(Guid.NewGuid(), normalized);
            if (!_names.TryAdd(normalized, workspace)) throw new ArgumentException("Ce nom de projet existe déjà.");
            return Task.FromResult(workspace);
        }
    }
}
