using Alycia.Modules.Workspaces.Domain;

namespace Alycia.Modules.Workspaces.Application;

public sealed class GetWorkspaceService(IWorkspaceRepository repository)
{
    public Task<Workspace?> ExecuteAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Workspace ID must not be empty.", nameof(id));
        }

        return repository.GetByIdAsync(id, cancellationToken);
    }
}
