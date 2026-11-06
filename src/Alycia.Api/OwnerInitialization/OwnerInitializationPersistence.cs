using Alycia.Modules.Identity.Domain;
using Alycia.Modules.Identity.Infrastructure;
using Alycia.Modules.Workspaces.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Alycia.Api.OwnerInitialization;

// Cross-module infrastructure orchestration only. The bootstrap service verifies
// the local secret and owner inputs before invoking this persistence operation.
public sealed class OwnerInitializationPersistence(string connectionString)
{
    public async Task<Guid> InitializeAsync(
        User owner,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(owner);
        if (string.IsNullOrWhiteSpace(owner.PasswordHash))
        {
            throw new ArgumentException("A password verifier is required.", nameof(owner));
        }

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var ownerId = await InitializeInTransactionAsync(
            owner, connection, transaction, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return ownerId;
    }

    // Used only after successful secret verification; secret consumption and
    // ownership assignment are committed together by the caller.
    public async Task<Guid> InitializeInTransactionAsync(
        User owner,
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(transaction);
        if (string.IsNullOrWhiteSpace(owner.PasswordHash))
        {
            throw new ArgumentException("A password verifier is required.", nameof(owner));
        }

        var identityOptions = new DbContextOptionsBuilder<IdentityDbContext>()
            .UseNpgsql(connection, options =>
                options.MigrationsHistoryTable("__EFMigrationsHistory", "identity"))
            .Options;
        var workspacesOptions = new DbContextOptionsBuilder<WorkspacesDbContext>()
            .UseNpgsql(connection, options =>
                options.MigrationsHistoryTable("__EFMigrationsHistory", "workspaces"))
            .Options;

        await using var identity = new IdentityDbContext(identityOptions);
        await using var workspaces = new WorkspacesDbContext(workspacesOptions);
        await identity.Database.UseTransactionAsync(transaction, cancellationToken);
        await workspaces.Database.UseTransactionAsync(transaction, cancellationToken);

        // The database unique owner slot prevents two concurrent initializations.
        // Its insert is still uncommitted while the Workspace backfill runs.
        await new EfUserRepository(identity).AddAsync(owner, cancellationToken);

        // Blocks concurrent Workspace INSERT/UPDATE until the backfill commits.
        // Existing S6 rows survive; no cross-module physical FK is introduced.
        await workspaces.Database.ExecuteSqlRawAsync(
            "LOCK TABLE workspaces.workspaces IN SHARE ROW EXCLUSIVE MODE",
            cancellationToken);

        // A prior, inconsistent owner assignment must fail closed, not be overwritten.
        if (await workspaces.Workspaces.AnyAsync(
            workspace => workspace.OwnerUserId != null, cancellationToken))
        {
            throw new InvalidOperationException("Workspace ownership is inconsistent.");
        }

        await workspaces.Workspaces
            .Where(workspace => workspace.OwnerUserId == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(
                workspace => workspace.OwnerUserId, owner.Id), cancellationToken);

        return owner.Id;
    }
}
