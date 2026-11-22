using Alycia.Contracts.Indexing;
using System.Text;
using Alycia.Modules.Chat.Application;
using Alycia.Modules.Chat.Domain;
using Alycia.Modules.Chat.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Alycia.Modules.Chat.Tests;

[Collection("Foreground indexing")]
public sealed class PersistentEmbeddingTests
{
    [PostgresFact]
    public async Task BatchesResumeWithoutQuotaAndKeepHistoryAndSourcesIsolated()
    {
        var connection = Environment.GetEnvironmentVariable("ALYCIA_CHAT_TEST_CONNECTION")!;
        var name = "alycia_embeddings_" + Guid.NewGuid().ToString("N");
        var root = Path.Combine(Path.GetTempPath(), name);
        await using var admin = new NpgsqlConnection(connection); await admin.OpenAsync();
        await using (var create = new NpgsqlCommand($"CREATE DATABASE {name}", admin)) await create.ExecuteNonQueryAsync();
        var isolated = new NpgsqlConnectionStringBuilder(connection) { Database = name, Pooling = false }.ToString();
        var dbOptions = new DbContextOptionsBuilder<ChatDbContext>().UseNpgsql(isolated,
            provider => provider.MigrationsHistoryTable("__EFMigrationsHistory", "chat")).Options;
        try
        {
            await using var database = new ChatDbContext(dbOptions);
            await database.Database.MigrateAsync();
            if (Environment.GetEnvironmentVariable("ALYCIA_TEST_PGVECTOR") == "1")
                await database.Database.ExecuteSqlRawAsync("CREATE EXTENSION vector");
            var options = new ChatOptions { RetrievalMode = "hybrid", EmbeddingModel = "local-embed", EmbeddingChunkCharacters = 128 };
            var chat = Conversation.Create("Sources", "demo", "");
            chat.Configure("demo", "", "", ConversationConfiguration.Serialize(options));
            var other = Conversation.Create("Autre", "demo", "");
            database.Conversations.AddRange(chat, other); await database.SaveChangesAsync();
            var storage = new FileStorage(root); var reader = new ProjectSourceReader(storage);
            var text = string.Join('\n', Enumerable.Range(0, 3000).Select(i => $"Passage {i:D6} : " + new string('x', 120)));
            var file = Assert.Single(await new FileImport(database, storage, ChatFixture.Profiles(database), new ConversationFileChanges(database)).ImportAsync(chat.Id, Guid.NewGuid(),
                [new("notes.txt", new MemoryStream(Encoding.UTF8.GetBytes(text)))], default));
            var request = Guid.NewGuid();
            database.Messages.AddRange(new ChatMessage(chat.Id, 1, request, "user", "La couleur retenue est turquoise."),
                new ChatMessage(chat.Id, 2, request, "assistant", "Je propose une variante indigo."));
            chat.CompleteTurn(); await database.SaveChangesAsync();
            var encoder = new Encoder { FailAt = 2 };
            var store = ChatFixture.Embeddings(database);
            var worker = new BackgroundEmbeddingIndex(ChatFixture.IndexFiles(database, reader), ChatFixture.LiveFiles(database), store, encoder, IndexParameters.Default, new ConversationIndexOwners(new(database.Database)), ChatFixture.Profiles(database), ChatFixture.Profiles(database), ChatFixture.Foreground);
            await store.QueueAsync(chat.Id, false, IndexParameters.Capture(options), default);
            await worker.ProcessAsync(Assert.Single(await store.JobsAsync(default)), default);
            var failed = Assert.Single(await store.JobsAsync(default));
            Assert.Equal("failed", failed.Progress.State);
            Assert.Equal(16, encoder.Texts);

            // Resume from durable rows with a fresh service instance, without repeating the first batch.
            encoder.FailAt = 0;
            await store.QueueAsync(chat.Id, false, IndexParameters.Capture(options), default, retry: true);
            var resumed = new BackgroundEmbeddingIndex(ChatFixture.IndexFiles(database, reader), ChatFixture.LiveFiles(database), ChatFixture.Embeddings(database), encoder, IndexParameters.Default, new ConversationIndexOwners(new(database.Database)), ChatFixture.Profiles(database), ChatFixture.Profiles(database), ChatFixture.Foreground);
            using (var interrupted = new CancellationTokenSource())
            {
                encoder.CancelAt = 4; encoder.Cancel = interrupted.Cancel;
                var queued = Assert.Single(await store.JobsAsync(default));
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => resumed.ProcessAsync(queued, interrupted.Token));
            }
            Assert.Equal("running", Assert.Single(await store.JobsAsync(default)).Progress.State);
            encoder.CancelAt = 0;
            // A restarted worker automatically resumes a running job; no new queue request is needed.
            await resumed.ProcessAsync(Assert.Single(await store.JobsAsync(default)), default);
            var done = Assert.Single(await store.JobsAsync(default));
            Assert.Equal("completed", done.Progress.State);
            Assert.Equal(3, done.Progress.Completed);
            await store.PauseAsync(chat.Id, false, default);
            await resumed.DiscoverAsync(default);
            var paused = Assert.Single(await store.JobsAsync(default));
            Assert.Equal("paused", paused.Progress.State);
            Assert.False(await store.ProgressAsync(done, "running", 0, 3, "obsolete", "", default));
            await resumed.ProcessAsync(paused, default);
            Assert.Equal("paused", Assert.Single(await store.JobsAsync(default)).Progress.State);
            await using var count = new NpgsqlCommand("SELECT count(*) FROM chat.embedding_vectors", new NpgsqlConnection(isolated));
            await count.Connection!.OpenAsync();
            try
            {
                var stored = (long)(await count.ExecuteScalarAsync())!;
                Assert.True(stored > 2048);
                Assert.Equal(stored, encoder.Texts);
            }
            finally { await count.Connection.DisposeAsync(); }
            var index = new ProjectIndexService(new ConversationIndexOwners(new(database.Database)), new PassageCacheDirectory(storage.Root), ChatFixture.IndexFiles(database, reader), ChatFixture.LiveFiles(database), IndexParameters.Capture(options));
            var snapshot = await index.BuildAsync(chat.Id, default);
            var searched = await resumed.SearchAsync(chat.Id, "Passage", EmbeddingQuery.Capture(options), snapshot.Chunks, default);
            Assert.Equal(snapshot.Chunks.Count, searched.Hits);
            Assert.Single(searched.Calls); // Only the query is embedded when sending a message.
            var history = await database.Messages.Where(m => m.ConversationId == chat.Id).OrderBy(m => m.Sequence).ToArrayAsync();
            Assert.Equal(2, (await resumed.SearchHistoryAsync(chat.Id, "couleur", EmbeddingQuery.Capture(options), history.Select(m => m.ToHistory()).ToArray(), default)).Hits);
            await Assert.ThrowsAsync<ArgumentException>(() => resumed.SearchHistoryAsync(other.Id, "couleur", EmbeddingQuery.Capture(options), history.Select(m => m.ToHistory()).ToArray(), default));
            Assert.Empty((await index.BuildAsync(other.Id, default)).Chunks);

            // Model identity changes select a separate vector space; old vectors are never mixed in.
            encoder.Identity = "local-embed:new-digest";
            Assert.Equal(0, (await resumed.SearchAsync(chat.Id, "Passage", EmbeddingQuery.Capture(options), snapshot.Chunks, default)).Hits);
            encoder.Identity = "local-embed:digest";
            var enriched = options.Copy(); enriched.HierarchicalIndexEnabled = true;
            enriched.EmbeddingDocumentPrefix = "passage: "; enriched.EmbeddingQueryPrefix = "query: ";
            chat.Configure(chat.Model, chat.SystemPrompt, chat.ContextNotes, ConversationConfiguration.Serialize(enriched));
            await database.SaveChangesAsync();
            var hierarchical = await index.BuildAsync(chat.Id, default);
            Assert.Equal(0, (await resumed.SearchAsync(chat.Id, "Passage", EmbeddingQuery.Capture(enriched), hierarchical.Chunks, default)).Hits);
            await store.QueueAsync(chat.Id, false, IndexParameters.Capture(enriched), default, retry: true);
            await resumed.ProcessAsync(Assert.Single(await store.JobsAsync(default)), default);
            Assert.Equal(hierarchical.Chunks.Count, (await resumed.SearchAsync(chat.Id, "Passage", EmbeddingQuery.Capture(enriched), hierarchical.Chunks, default)).Hits);
            Assert.Equal(snapshot.Chunks.Count, (await resumed.SearchAsync(chat.Id, "Passage", EmbeddingQuery.Capture(options), snapshot.Chunks, default)).Hits);
            Assert.Contains(encoder.Inputs, value => value.StartsWith("passage: "));
            Assert.Contains(encoder.Inputs, value => value.StartsWith("query: "));
            var attachment = await database.Attachments.SingleAsync(f => f.Id == file.Id);
            attachment.ContextMode = "excluded"; await database.SaveChangesAsync();
            Assert.Empty((await index.BuildAsync(chat.Id, default)).Chunks);

            // No former eight-million-character quota for the lexical/source index.
            var large = new string('z', 8_100_000);
            await new FileImport(database, storage, ChatFixture.Profiles(database), new ConversationFileChanges(database)).ImportAsync(chat.Id, Guid.NewGuid(), [new("large.txt", new MemoryStream(Encoding.UTF8.GetBytes(large)))], default);
            var largeIndex = await index.BuildAsync(chat.Id, default);
            Assert.Equal(1, largeIndex.IndexedFiles);
            Assert.Empty(largeIndex.Omissions);

            var importSettings = new ImportSettingsStore(database);
            var restrictive = new ImportLimits(FileMiB: 1, BatchMiB: 2, BatchEntries: 1, StoredFiles: 1, StoredMiB: 2);
            await importSettings.SaveAsync(restrictive, default);
            Assert.Equal(restrictive, await new ImportSettingsStore(database).LoadAsync(default));
            var importer = new FileImport(database, storage, ChatFixture.Profiles(database), new ConversationFileChanges(database), importSettings);
            await Assert.ThrowsAsync<ArgumentException>(() => importer.ImportAsync(chat.Id, Guid.NewGuid(),
                [new("over.txt", new MemoryStream(new byte[1024 * 1024 + 1]))], default));
            await Assert.ThrowsAsync<ArgumentException>(() => importer.ImportAsync(chat.Id, Guid.NewGuid(),
                [new("extra.txt", new MemoryStream(Encoding.UTF8.GetBytes("quota")))], default));
            await importSettings.SaveAsync(restrictive with { StoredFiles = 0, StoredMiB = 0 }, default);
            Assert.Single(await importer.ImportAsync(chat.Id, Guid.NewGuid(),
                [new("extra.txt", new MemoryStream(Encoding.UTF8.GetBytes("quota")))], default));

            database.Conversations.Remove(chat); await database.SaveChangesAsync();
            await using var verify = new NpgsqlConnection(isolated); await verify.OpenAsync();
            await using var remaining = new NpgsqlCommand("SELECT count(*) FROM chat.embedding_vectors", verify);
            Assert.Equal(0L, (long)(await remaining.ExecuteScalarAsync())!);
        }
        finally
        {
            NpgsqlConnection.ClearAllPools();
            await using var drop = new NpgsqlCommand($"DROP DATABASE {name} WITH (FORCE)", admin); await drop.ExecuteNonQueryAsync();
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    private sealed class Encoder : ILocalEmbeddings
    {
        public string Identity = "local-embed:digest";
        public List<string> Inputs { get; } = [];
        public int FailAt; public int Calls; public int Texts;
        public int CancelAt; public Action? Cancel;
        public Task<string> IdentityAsync(string model, CancellationToken ct) => Task.FromResult(Identity);
        public Task<EmbeddingBatch> EmbedAsync(string model, IReadOnlyList<string> input, CancellationToken ct)
        {
            if (++Calls == FailAt) throw new ChatModelException("Interruption simulée");
            if (Calls == CancelAt) { Cancel!(); ct.ThrowIfCancellationRequested(); }
            Inputs.AddRange(input);
            Texts += input.Count;
            return Task.FromResult(new EmbeddingBatch(input.Select(_ => new float[] { 1, 0, 0 }).ToArray(), new(10, 0, 1, null, "embedding")));
        }
    }
}
