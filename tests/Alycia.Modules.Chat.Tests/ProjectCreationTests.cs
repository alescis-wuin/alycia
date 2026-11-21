using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Alycia.Contracts.Workspaces;
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
        var identities = new WorkspaceIdentityDirectory(session);
        return new(session, identities, new WorkspaceProfileStore(session, identities), new ProjectNoteOrigins(database));
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

    [PostgresFact]
    public Task DiscoveryReadsEachOwnerOnceAcrossPageBoundaries() =>
        WithDatabaseAsync(async (options, _) =>
        {
            await using var db = new ChatDbContext(options);
            var owners = Enumerable.Range(0, 129).Select(n =>
                Alycia.Modules.Chat.Domain.Conversation.Create("C" + n, "local", "")).ToArray();
            db.Conversations.AddRange(owners);
            await db.SaveChangesAsync();
            var read = new ConversationIndexOwners(db);
            var first = await read.PageAsync(null, 128, default);
            Assert.Equal(128, first.Length);
            var last = Assert.Single(await read.PageAsync(first[^1].Id, 128, default));
            Assert.Empty(await read.PageAsync(last.Id, 128, default));
            Assert.Equal(owners.Select(o => o.Id).Order(), first.Select(o => o.Id).Append(last.Id).Order());

            var catalog = Catalog(db);
            var a = await catalog.CreateAsync("Page A", default);
            var b = await catalog.CreateAsync("Page B", default);
            var profile = await catalog.GetAsync(b.Id, false, default);
            await catalog.UpdateAsync(b.Id, profile.Revision, profile.Instructions, profile.DefaultModel, true, default);
            var active = ChatFixture.Profiles(db);
            var item = Assert.Single(await active.ActivePageAsync(null, 1, default));
            Assert.Equal(a.Id, item.Id);
            Assert.Empty(await active.ActivePageAsync(item.Id, 1, default));
        });

    [PostgresFact]
    public Task Lost_commit_acknowledgement_leaves_one_recoverable_identity_and_profile() =>
        WithDatabaseAsync(async (options, _) =>
        {
            var fault = new DbContextOptionsBuilder<ChatDbContext>(options).AddInterceptors(new LoseAcknowledgement()).Options;
            await using var failing = new ChatDbContext(fault);
            await Assert.ThrowsAsync<IOException>(() => Catalog(failing).CreateAsync("Persisted", default));
            await using var fresh = new ChatDbContext(options);
            var catalog = Catalog(fresh);
            var created = Assert.Single(await catalog.ListAsync(default));
            Assert.Equal("Persisted", created.Name);
            Assert.Equal(0, (await catalog.GetAsync(created.Id, false, default)).Revision);
            await Assert.ThrowsAsync<ArgumentException>(() => catalog.CreateAsync("PERSISTED", default));
            Assert.Single(await catalog.ListAsync(default));
        });

    private sealed class LoseAcknowledgement : DbTransactionInterceptor
    {
        public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData,
            CancellationToken cancellationToken = default) => throw new IOException("Accusé de commit perdu.");
    }

    [PostgresFact]
    public Task Competing_profile_writes_preserve_one_revision_and_note_provenance() =>
        WithDatabaseAsync(async (options, _) =>
        {
            await using var a = new ChatDbContext(options);
            await using var b = new ChatDbContext(options);
            var project = await Catalog(a).CreateAsync("Notes", default);
            var other = await Catalog(b).CreateAsync("Voisin", default);
            var owner = Alycia.Modules.Chat.Domain.Conversation.Create("C", "local", "");
            owner.AssignWorkspace(project.Id);
            a.Add(owner);
            await a.SaveChangesAsync();
            await Assert.ThrowsAsync<ArgumentException>(() => Catalog(b).ChangeNoteAsync(other.Id, null, 0, "hors portée", owner.Id, null, false, default));
            await Assert.ThrowsAsync<ArgumentException>(() => Catalog(b).ChangeNoteAsync(project.Id, null, 0, "absent", owner.Id, 1, false, default));
            var recorded = await Catalog(a).ChangeNoteAsync(project.Id, null, 0, "décision", owner.Id, null, false, default);
            Assert.Equal(owner.Id, Assert.Single(recorded.Notes).OriginConversationId);
            async Task<bool> Write(ChatDbContext db, string instructions)
            {
                try { await Catalog(db).UpdateAsync(project.Id, 1, instructions, "local", false, default); return true; }
                catch (ChatConflictException) { return false; }
            }
            Assert.Single((await Task.WhenAll(Write(a, "A"), Write(b, "B"))).Where(success => success));
            var saved = await Catalog(b).GetAsync(project.Id, false, default);
            Assert.Equal(2, saved.Revision);
            Assert.Equal("décision", Assert.Single(saved.Notes).Text);
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
