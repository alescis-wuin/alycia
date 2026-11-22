using Alycia.Modules.Workspaces.Domain;

namespace Alycia.Modules.Workspaces.Application;

public sealed class CreateWorkspaceService(IWorkspaceRepository repository)
{
    public async Task<Workspace> ExecuteAsync(
        string? name,
        CancellationToken cancellationToken = default)
    {
        var workspace = Workspace.Create(name);

        if (await repository.ExistsByNameAsync(workspace.Name, cancellationToken))
        {
            throw new WorkspaceNameConflictException();
        }

        // Infrastructure must enforce uniqueness again on the actual insert.
        await repository.AddAsync(workspace, cancellationToken);
        return workspace;
    }
}
