using Alycia.Modules.Workspaces.Domain;
using System.Text;
using Alycia.Contracts.Workspaces;
using Alycia.Modules.Chat.Application;
using Alycia.Modules.Chat.Domain;
using Alycia.Modules.Chat.Infrastructure;
using Alycia.Modules.Testing;
using Alycia.Modules.Workspaces.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Alycia.Modules.Chat.Tests;

public sealed class OwnerContractTests
{
    [PostgresFact]
    public Task Import_limits_preserve_defaults_overrides_and_cancellation_in_both_adapters() =>
        ProjectCreationTests.WithDatabaseAsync(async (options, _) =>
        {
            await using var db = new ChatDbContext(options);
            var store = new ImportSettingsStore(db);
            foreach (var limits in new[] { new ImportLimits(), new ImportLimits(FileMiB: 2, BatchMiB: 4) })
            {
                if (limits != new ImportLimits()) await store.SaveAsync(limits, default);
                foreach (var reader in new IImportLimitsReader[] { store, new FixedImportLimits(limits) })
                {
                    Assert.Equal(limits, await reader.LoadAsync(default));
                    await Assert.ThrowsAnyAsync<OperationCanceledException>(() => reader.LoadAsync(new CancellationToken(true)));
                }
            }
        });

    [PostgresFact]
    public Task Conversation_pages_and_tool_policies_agree_on_scope_cursors_and_cancellation() =>
        ProjectCreationTests.WithDatabaseAsync(async (options, _) =>
        {
            await using var db = new ChatDbContext(options);
            var primary = Conversation.Create("A", "local", "");
            var empty = Conversation.Create("B", "local", "");
            var project = new WorkspaceProfile { Id = Guid.NewGuid() };
            primary.AssignWorkspace(project.Id);
            db.Add(project);
            primary.Configure("local", "", "", ConversationConfiguration.Serialize(new ChatOptions
            {
                UseProjectTools = true,
                RoutingMode = "memory",
                RetrievalMode = "hybrid",
                EmbedHistory = true,
                EmbeddingModel = "projection-fixture"
            }));
            var turn = Guid.NewGuid();
            var ask = new ChatMessage(primary.Id, 1, turn, "user", "A");
            var reply = new ChatMessage(primary.Id, 2, turn, "assistant", "B");
            primary.CompleteTurn();
            db.AddRange(primary, empty, ask, reply);
            await db.SaveChangesAsync();
            var ids = new[] { primary.Id, empty.Id }.Order().ToArray();
            var mock = new SnapshotConversations(new[] { primary, empty }.Select(c =>
                new ConversationIndexOwner(c.Id, c.WorkspaceId, c.Revision, c.SettingsJson == "{}" ? null : IndexParameters.Capture(ConversationConfiguration.Options(c, new())), c.MessageCount)),
                new[] { ask, reply }.Select(m => new IndexHistoryMessage(m.ConversationId, m.RequestId, m.Sequence, m.Role, m.Content, m.CreatedAtUtc)),
                new[] { primary, empty }.ToDictionary(c => c.Id, c => ToolConversationPolicy.Capture(ConversationConfiguration.Options(c, new()), c.MessageCount)));
            foreach (var adapter in new IConversationIndexOwners[] { new ConversationIndexOwners(new(db.Database)), mock })
            {
                Assert.Equal(ids[0], Assert.Single(await adapter.PageAsync(null, 1, default)).Id);
                Assert.Equal(ids[1], Assert.Single(await adapter.PageAsync(ids[0], 1, default)).Id);
                Assert.Empty(await adapter.PageAsync(ids[1], 1, default));
                Assert.True(await adapter.ExistsAsync(primary.Id, default));
                Assert.False(await adapter.ExistsAsync(Guid.Empty, default));
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => adapter.ExistsAsync(primary.Id, new(true)));
                var indexOwner = await adapter.FindAsync(primary.Id, default);
                Assert.Equal(2, indexOwner!.MessageCount);
                Assert.Equal("projection-fixture", indexOwner.Override?.EmbeddingModel);
                Assert.Null((await adapter.FindAsync(empty.Id, default))!.Override);
                Assert.Null(await adapter.FindAsync(Guid.Empty, default));
                var message = Assert.Single(await adapter.HistoryPageAsync(primary.Id, 1, 2, 1, default));
                Assert.Equal((turn, 2, "assistant", "B"), (message.RequestId, message.Sequence, message.Role, message.Content));
                Assert.Empty(await adapter.HistoryPageAsync(empty.Id, 0, 2, 1, default));
                await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => adapter.PageAsync(null, 0, default));
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => adapter.HistoryPageAsync(primary.Id, 0, 2, 1, new(true)));
            }
            await using (var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead))
                foreach (var lookup in new IConversationWorkspaceLookup[] { new ConversationWorkspaceLookup(new(db.Database)), mock })
                {
                    Assert.Equal(project.Id, await lookup.FindWorkspaceAsync(primary.Id, default));
                    Assert.Null(await lookup.FindWorkspaceAsync(empty.Id, default));
                    Assert.Null(await lookup.FindWorkspaceAsync(Guid.Empty, default));
                    await Assert.ThrowsAnyAsync<OperationCanceledException>(() => lookup.FindWorkspaceAsync(primary.Id, new(true)));
                }
            foreach (var adapter in new IToolConversationPolicy[] { new ToolConversationPolicyReader(db, new()), mock })
            {
                Assert.True(await adapter.HasHistoryAsync(primary.Id, default));
                Assert.False(await adapter.HasHistoryAsync(empty.Id, default));
                var policy = await adapter.ReadAsync(primary.Id, default);
                Assert.NotNull(policy);
                Assert.Equal(2, policy.MessageCount);
                Assert.True(policy.ToolsEnabled);
                Assert.Equal(1, policy.FirstVisibleSequence);
                Assert.Equal("projection-fixture", policy.HistoryEmbedding?.Model);
                var defaults = await adapter.ReadAsync(empty.Id, default);
                Assert.Null(defaults!.FirstVisibleSequence);
                Assert.Null(defaults.HistoryEmbedding);
                Assert.Null(await adapter.ReadAsync(Guid.Empty, default));
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => adapter.ReadAsync(primary.Id, new(true)));
            }
        });

    [PostgresFact]
    public Task Workspace_identity_and_active_pages_share_the_same_behavior_with_their_doubles() =>
        ProjectCreationTests.WithDatabaseAsync(async (options, _) =>
        {
            await using var db = new ChatDbContext(options);
            foreach (var directory in new IWorkspaceIdentityDirectory[] { new WorkspaceIdentityDirectory(new(db.Database)), new MemoryWorkspaceDirectory() })
            {
                var beta = await directory.CreateAsync("  Beta  ", default);
                var alpha = await directory.CreateAsync("Alpha", default);
                Assert.Equal("Beta", beta.Name);
                Assert.True(await directory.ExistsAsync(alpha.Id, default));
                Assert.False(await directory.ExistsAsync(Guid.Empty, default));
                Assert.Equal(new[] { alpha, beta }, await directory.ListAsync(default));
                await Assert.ThrowsAsync<ArgumentException>(() => directory.CreateAsync("beta", default));
                await Assert.ThrowsAsync<ArgumentException>(() => directory.CreateAsync(" ", default));
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => directory.ListAsync(new(true)));
            }
            var visible = new WorkspaceProfile { Id = Guid.NewGuid(), DefaultModel = "demo" };
            var hidden = new WorkspaceProfile { Id = Guid.NewGuid(), Archived = true };
            db.AddRange(visible, hidden);
            await db.SaveChangesAsync();
            var session = new Alycia.Platform.Persistence.RelationalSession(db.Database);
            var identities = new WorkspaceIdentityDirectory(session);
            var lazy = await identities.CreateAsync("lazy", default);
            var realCatalog = new ProjectCatalog(session, identities, ChatFixture.Profiles(db), new ProjectNoteOrigins(db));
            var mockDefaults = new SnapshotProjectDefaults([
                new(visible.Id, "", "demo", false, visible.Revision, []),
                new(hidden.Id, "", "", true, hidden.Revision, [])], [lazy.Id]);
            foreach (var projects in new IProjectIndexOwners[] { ChatFixture.Profiles(db), new SnapshotProjects([new(visible.Id, visible.Revision)]) })
            {
                Assert.Equal(new(visible.Id, visible.Revision), Assert.Single(await projects.ActivePageAsync(null, 1, default)));
                Assert.Empty(await projects.ActivePageAsync(visible.Id, 512, default));
                await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => projects.ActivePageAsync(null, 513, default));
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => projects.ActivePageAsync(null, 1, new(true)));
            }
            foreach (var defaults in new IProjectModelDefaults[] { realCatalog, mockDefaults })
            {
                Assert.Equal("demo", await defaults.ActiveDefaultModelAsync(visible.Id, default));
                Assert.Equal("", await defaults.ActiveDefaultModelAsync(lazy.Id, default));
                await Assert.ThrowsAsync<ArgumentException>(() => defaults.ActiveDefaultModelAsync(hidden.Id, default));
                await Assert.ThrowsAsync<ArgumentException>(() => defaults.ActiveDefaultModelAsync(Guid.Empty, default));
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => defaults.ActiveDefaultModelAsync(visible.Id, new(true)));
            }
        });

    [PostgresFact]
    public Task Document_tools_live_permissions_and_index_use_the_same_scope_on_both_adapters() =>
        ProjectCreationTests.WithDatabaseAsync(async (options, _) =>
        {
            await using var db = new ChatDbContext(options);
            var chat = Conversation.Create("Sources", "local", "");
            var other = Conversation.Create("Autre", "local", "");
            db.AddRange(chat, other);
            await db.SaveChangesAsync();
            var root = Directory.CreateTempSubdirectory("alycia-contract-");
            try
            {
                var storage = new FileStorage(root.FullName);
                var reader = new ProjectSourceReader(storage);
                var importer = new FileImport(db, storage, ChatFixture.Profiles(db), new ConversationFileChanges(db));
                var file = Assert.Single(await importer.ImportAsync(chat.Id, Guid.NewGuid(), [new("exemple.txt", new MemoryStream(Encoding.UTF8.GetBytes("Texte local.")))], default));
                var live = ChatFixture.LiveFiles(db);
                var real = new AuthorizedToolSources(db, reader, new ToolConversationPolicyReader(db, new()), live, ChatFixture.DocumentScope);
                var original = await real.ReadAsync(chat.Id, file.Id, default);
                var entry = Assert.Single(await real.EntriesAsync(chat.Id, default));
                var mock = new SnapshotToolSources([new(chat.Id, false, entry, original, real.Declarations(original, default).ToArray())]);
                foreach (var sources in new IAuthorizedToolSources[] { real, mock })
                {
                    Assert.True(await sources.HasFilesAsync(chat.Id, default));
                    Assert.Empty(await sources.EntriesAsync(other.Id, default));
                    Assert.Equal(original, await sources.ReadAsync(chat.Id, file.Id, default));
                    Assert.All(sources.Declarations(original, default), chunk => Assert.Equal(file.Id, chunk.FileId));
                    await Assert.ThrowsAsync<ArgumentException>(() => sources.ReadAsync(other.Id, file.Id, default));
                    await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sources.ReadAsync(chat.Id, file.Id, new(true)));
                }
                foreach (var access in new ILiveIndexFileAccess[] { live, mock })
                {
                    Assert.True(await access.IsAllowedAsync(chat.Id, false, file.Id, default));
                    Assert.False(await access.IsAllowedAsync(other.Id, false, file.Id, default));
                    Assert.Empty(await access.CurrentEntriesAsync(chat.Id, [Guid.Empty], default));
                    Assert.Equal(entry, Assert.Single(await access.CurrentEntriesAsync(chat.Id, [file.Id], default)));
                }
                var index = new ProjectIndexService(new ConversationIndexOwners(new(db.Database)), new PassageCacheDirectory(storage.Root), ChatFixture.IndexFiles(db, reader), live);
                var passages = (await index.BuildAsync(chat.Id, default)).Chunks;
                Assert.NotEmpty(passages);
                foreach (var adapter in new IProjectIndex[] { index, new SnapshotIndex(chat.Id, passages, other.Id) })
                {
                    Assert.Empty((await adapter.BuildScopedAsync(chat.Id, new HashSet<Guid>(), default)).Chunks);
                    Assert.Empty((await adapter.BuildScopedAsync(other.Id, new HashSet<Guid> { file.Id }, default)).Chunks);
                    await Assert.ThrowsAsync<ConversationNotFoundException>(() => adapter.BuildScopedAsync(Guid.Empty, null, default));
                    var scoped = await adapter.BuildScopedAsync(chat.Id, new HashSet<Guid> { file.Id, Guid.Empty }, default);
                    Assert.Equal(1, scoped.EligibleFiles);
                    Assert.Equal(1, scoped.IndexedFiles);
                    var chunks = scoped.Chunks;
                    Assert.Equal(passages.Select(c => c.Text), chunks.Select(c => c.Text));
                    Assert.All(chunks, chunk => Assert.Equal(file.Id, chunk.FileId));
                    await Assert.ThrowsAnyAsync<OperationCanceledException>(() => adapter.BuildScopedAsync(chat.Id, null, new(true)));
                }
            }
            finally { root.Delete(true); }
        });
}
