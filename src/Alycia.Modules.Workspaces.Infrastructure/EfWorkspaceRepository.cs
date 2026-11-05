using Alycia.Modules.Workspaces.Application;
using Alycia.Modules.Workspaces.Domain;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Alycia.Modules.Workspaces.Infrastructure;

public sealed class EfWorkspaceRepository(WorkspacesDbContext dbContext)
    : IWorkspaceRepository
{
    public Task<bool> ExistsByNameAsync(
        string name,
        CancellationToken cancellationToken = default)
    {
        // The citext column and unique index apply the same case-insensitive rule.
        return dbContext.Workspaces.AsNoTracking()
            .AnyAsync(workspace => workspace.Name == name, cancellationToken);
    }

    public Task<Workspace?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        return dbContext.Workspaces.AsNoTracking()
            .SingleOrDefaultAsync(workspace => workspace.Id == id, cancellationToken);
    }

    public async Task AddAsync(
        Workspace workspace,
        CancellationToken cancellationToken = default)
    {
        dbContext.Workspaces.Add(workspace);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (
            exception.InnerException is PostgresException error &&
            error.SqlState == PostgresErrorCodes.UniqueViolation &&
            error.ConstraintName == "ux_workspaces_name")
        {
            // Handles both pre-existing names and concurrent inserts.
            throw new WorkspaceNameConflictException();
        }
    }
}
