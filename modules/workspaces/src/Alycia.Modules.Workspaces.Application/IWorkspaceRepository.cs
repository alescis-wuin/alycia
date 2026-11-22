using Alycia.Modules.Workspaces.Domain;

namespace Alycia.Modules.Workspaces.Application;

public interface IWorkspaceRepository
{
    Task<bool> ExistsByNameAsync(string name, CancellationToken cancellationToken = default);
    Task<Workspace?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task AddAsync(Workspace workspace, CancellationToken cancellationToken = default);
}
