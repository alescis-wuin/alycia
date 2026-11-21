using Alycia.Platform.Persistence;
using Alycia.Modules.Chat.Application;
using Alycia.Modules.Chat.Infrastructure;
using Alycia.Modules.Workspaces.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Alycia.Modules.Chat.Tests;

public sealed class ProjectCreationTests
{
    private static ProjectCatalog Catalog(ChatDbContext database)
    {
        var session = new RelationalSession(database.Database);
        return new(database, session, new WorkspaceIdentityDirectory(session), ChatFixture.Gate);
    }

    [PostgresFact]
    public Task CompetingCreatorsCommitExactlyOneMatchingWorkspaceAndProfile() =>
        WithDatabaseAsync(async (options, workspaces) =>
        {
            await using var first = new ChatDbContext(options);
            await using var second = new ChatDbContext(options);
            const string name = "Créations concurrentes";

            async Task<ProjectEntry?> CreateOrRejectAsync(ChatDbContext db)
            {
                try
                {
                    return await Catalog(db).CreateAsync(name, default);
                }
                catch (ArgumentException error)
                {
                    Assert.Equal("Ce nom de projet existe déjà.", error.Message);
                    return null;
                }
            }

            // Release both transactions only after PostgreSQL has observed both attempts to insert.
            await using var gate = await workspaces.Database.BeginTransactionAsync();
            await workspaces.Database.ExecuteSqlRawAsync("LOCK TABLE workspaces.workspaces IN SHARE MODE");
            var attempts = Task.WhenAll(CreateOrRejectAsync(first), CreateOrRejectAsync(second));
            ProjectEntry?[] results;
            try
            {
                using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                const string queuedWriters = """
                    SELECT count(*)::int AS "Value" FROM pg_locks
                    WHERE database = (SELECT oid FROM pg_database WHERE datname = current_database())
                      AND relation = 'workspaces.workspaces'::regclass AND mode = 'RowExclusiveLock' AND NOT granted
                    """;
                while (await workspaces.Database.SqlQueryRaw<int>(queuedWriters).SingleAsync(deadline.Token) < 2)
                    await Task.Delay(20, deadline.Token);
            }
            finally
            {
                await gate.RollbackAsync();
                results = await attempts;
            }

            var created = Assert.Single(results.OfType<ProjectEntry>());
            Assert.Single(results, result => result is null);
            var canonical = await workspaces.Workspaces.AsNoTracking().SingleAsync();
            Assert.Equal(name, canonical.Name);
            Assert.Equal(created.Id, canonical.Id);
            await using var audit = new ChatDbContext(options);
            Assert.Equal(canonical.Id, (await audit.WorkspaceProfiles.AsNoTracking().SingleAsync()).Id);
        });

    [PostgresFact]
    public Task FailureInProfileInsertRollsBackCanonicalWorkspaceAndAllowsRetry() =>
        WithDatabaseAsync(async (options, workspaces) =>
        {
            const string name = "Panne d’écriture du profil";
            const string install = $$"""
                CREATE FUNCTION chat.reject_test_profile() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM workspaces.workspaces WHERE id = NEW."Id" AND name = '{{name}}') THEN
                        RAISE EXCEPTION 'test_profile_insert_after_workspace';
                    END IF;
                    RETURN NEW;
                END $$;
                CREATE TRIGGER reject_test_profile BEFORE INSERT ON chat.workspace_profiles
                    FOR EACH ROW EXECUTE FUNCTION chat.reject_test_profile();
                """;
            await using var audit = new ChatDbContext(options);
            await audit.Database.ExecuteSqlRawAsync(install);
            try
            {
                await using var failing = new ChatDbContext(options);
                var error = await Assert.ThrowsAsync<DbUpdateException>(() => Catalog(failing).CreateAsync(name, default));
                var cause = Assert.IsType<PostgresException>(error.InnerException);
                Assert.Equal("test_profile_insert_after_workspace", cause.MessageText);
                Assert.Equal(0, await workspaces.Workspaces.CountAsync());
                Assert.Equal(0, await audit.WorkspaceProfiles.CountAsync());
            }
            finally
            {
                await audit.Database.ExecuteSqlRawAsync("""
                    DROP TRIGGER reject_test_profile ON chat.workspace_profiles;
                    DROP FUNCTION chat.reject_test_profile();
                    """);
            }

            var retry = await Catalog(audit).CreateAsync(name, default);
            Assert.Equal(retry.Id, (await workspaces.Workspaces.AsNoTracking().SingleAsync()).Id);
            Assert.Equal(retry.Id, (await audit.WorkspaceProfiles.AsNoTracking().SingleAsync()).Id);
        });

    private static async Task WithDatabaseAsync(Func<DbContextOptions<ChatDbContext>, WorkspacesDbContext, Task> run)
    {
        var connection = Environment.GetEnvironmentVariable("ALYCIA_CHAT_TEST_CONNECTION")!;
        var name = "alycia_projects_test_" + Guid.NewGuid().ToString("N");
        await using var admin = new NpgsqlConnection(connection);
        await admin.OpenAsync();
        await using var create = new NpgsqlCommand($"CREATE DATABASE {name}", admin);
        await create.ExecuteNonQueryAsync();
        try
        {
            var isolated = new NpgsqlConnectionStringBuilder(connection) { Database = name, Pooling = false }.ToString();
            var options = new DbContextOptionsBuilder<ChatDbContext>().UseNpgsql(isolated,
                provider => provider.MigrationsHistoryTable("__EFMigrationsHistory", "chat")).Options;
            await using var workspaces = new WorkspacesDbContext(new DbContextOptionsBuilder<WorkspacesDbContext>().UseNpgsql(isolated,
                provider => provider.MigrationsHistoryTable("__EFMigrationsHistory", "workspaces")).Options);
            await workspaces.Database.MigrateAsync();
            await using var chat = new ChatDbContext(options);
            await chat.Database.MigrateAsync();
            await run(options, workspaces);
        }
        finally
        {
            await using var drop = new NpgsqlCommand($"DROP DATABASE {name} WITH (FORCE)", admin);
            await drop.ExecuteNonQueryAsync();
        }
    }
}
