using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Alycia.Modules.Workspaces.Infrastructure;

// Used only by dotnet-ef to scaffold migration files or migration SQL.
public sealed class WorkspacesDbContextFactory
    : IDesignTimeDbContextFactory<WorkspacesDbContext>
{
    public WorkspacesDbContext CreateDbContext(string[] args)
    {
        // Scaffolding and SQL generation do not open a database connection.
        var connectionString = Environment.GetEnvironmentVariable(
            "ALYCIA_WORKSPACES_CONNECTION")
            ?? "Host=localhost;Database=alycia;Username=alycia";

        var options = new DbContextOptionsBuilder<WorkspacesDbContext>()
            .UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "workspaces"))
            .Options;

        return new WorkspacesDbContext(options);
    }
}
