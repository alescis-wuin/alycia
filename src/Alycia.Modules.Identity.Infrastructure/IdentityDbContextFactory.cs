using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Alycia.Modules.Identity.Infrastructure;

// Scaffolds module-owned migrations without connecting to the database.
public sealed class IdentityDbContextFactory : IDesignTimeDbContextFactory<IdentityDbContext>
{
    public IdentityDbContext CreateDbContext(string[] args)
    {
        // E01 requires both contexts to use the same physical PostgreSQL database.
        var connectionString = Environment.GetEnvironmentVariable(
            "ALYCIA_WORKSPACES_CONNECTION")
            ?? "Host=localhost;Database=alycia;Username=alycia";

        var options = new DbContextOptionsBuilder<IdentityDbContext>()
            .UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "identity"))
            .Options;

        return new IdentityDbContext(options);
    }
}
