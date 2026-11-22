using Alycia.Modules.Workspaces.Application;
using Alycia.Modules.Workspaces.Domain;
using Alycia.Modules.Workspaces.Infrastructure;
using Alycia.Modules.Workspaces.Tests.Doubles;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Alycia.Modules.Workspaces.Tests;

public sealed class RepositoryContractTests
{
    private sealed class DatabaseFact : FactAttribute
    {
        public DatabaseFact()
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ALYCIA_CHAT_TEST_CONNECTION"))) Skip = "PostgreSQL de test requis.";
        }
    }

    [DatabaseFact]
    public async Task Workspace_repositories_agree_on_identity_case_uniqueness_and_cancelled_writes()
    {
        var admin = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("ALYCIA_CHAT_TEST_CONNECTION"));
        var isolated = new NpgsqlConnectionStringBuilder(admin.ConnectionString) { Database = "alycia_ws_" + Guid.NewGuid().ToString("N"), Pooling = false };
        var options = new DbContextOptionsBuilder<WorkspacesDbContext>().UseNpgsql(isolated.ConnectionString,
            pg => pg.MigrationsHistoryTable("__EFMigrationsHistory", "workspaces")).Options;
        await using var db = new WorkspacesDbContext(options);
        try
        {
            await db.Database.MigrateAsync();
            foreach (var repository in new IWorkspaceRepository[] { new EfWorkspaceRepository(db), new MemoryWorkspaceRepository() })
            {
                var workspace = Workspace.Create("Alpha");
                Assert.Null(await repository.GetByIdAsync(workspace.Id));
                await repository.AddAsync(workspace);
                Assert.Equal(workspace.Name, (await repository.GetByIdAsync(workspace.Id))!.Name);
                Assert.True(await repository.ExistsByNameAsync("ALPHA"));
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => repository.AddAsync(Workspace.Create("Beta"), new(true)));
                Assert.False(await repository.ExistsByNameAsync("Beta"));
                await Assert.ThrowsAsync<WorkspaceNameConflictException>(() => repository.AddAsync(Workspace.Create("aLpHa")));
                await repository.AddAsync(Workspace.Create("Gamma"));
                Assert.True(await repository.ExistsByNameAsync("Gamma"));
                Assert.False(await repository.ExistsByNameAsync("Beta"));
            }
        }
        finally { await db.Database.EnsureDeletedAsync(); }
    }
}
