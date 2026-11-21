using Alycia.Modules.Retrieval;
using Alycia.Contracts.Indexing;
using Alycia.Contracts.Memory;
using Alycia.Modules.Chat.Application;
using Alycia.Modules.Chat.Domain;
using Alycia.Modules.Chat.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Alycia.Modules.Chat.Tests;

public class TemporalMemoryIntegrationTests
{
    [PostgresFact]
    public Task A_missing_checkpoint_is_repaired_after_a_full_32_message_slice() =>
        ProjectCreationTests.WithDatabaseAsync(async (configuration, _) =>
        {
            await using var db = new ChatDbContext(configuration);
            var conversation = Conversation.Create("Reprise", "local", "");
            db.Conversations.Add(conversation);
            for (var turn = 0; turn < 18; turn++)
            {
                var request = Guid.NewGuid();
                db.Messages.AddRange(new ChatMessage(conversation.Id, turn * 2 + 1, request, "user", "source"),
                    new ChatMessage(conversation.Id, turn * 2 + 2, request, "assistant", "réponse"));
                conversation.CompleteTurn();
            }
            await db.SaveChangesAsync();
            var storage = new Alycia.Modules.Memory.TemporalFactStorage(db.Database);
            var worker = new TemporalMemoryStore(storage, new TemporalHistorySources(db, new() { TemporalMemoryEnabled = true }), new NoVectors());
            await worker.RefreshAsync(default);
            Assert.Equal(32, await storage.ContiguousThroughAsync(conversation.Id, 36, default));
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM chat.temporal_memory WHERE owner={conversation.Id} AND sequence=7");
            await worker.RefreshAsync(default);
            Assert.Equal(36, await storage.ContiguousThroughAsync(conversation.Id, 36, default));
            Assert.Equal(Enumerable.Range(1, 36), (await worker.ExportAsync(conversation.Id, 0, 64, default)).Select(f => f.Sequence));
        });

    [PostgresFact]
    public async Task WorkerRebuildsFromOriginalsAndScopesCorrectionsAndHistoricalViews()
    {
        var name = "alycia_memory_" + Guid.NewGuid().ToString("N");
        // Npgsql removes the password from ConnectionString after OpenAsync.
        var testConnection = Environment.GetEnvironmentVariable("ALYCIA_CHAT_TEST_CONNECTION")!;
        await using var admin = new NpgsqlConnection(testConnection);
        await admin.OpenAsync();
        await using (var create = new NpgsqlCommand($"CREATE DATABASE {name}", admin)) await create.ExecuteNonQueryAsync();
        try
        {
            var connection = new NpgsqlConnectionStringBuilder(testConnection) { Database = name, Pooling = false };
            var dbOptions = new DbContextOptionsBuilder<ChatDbContext>().UseNpgsql(connection.ToString(), p => p.MigrationsHistoryTable("__EFMigrationsHistory", "chat")).Options;
            await using var db = new ChatDbContext(dbOptions);
            await db.Database.MigrateAsync();
            var options = new ChatOptions { TemporalMemoryEnabled = true, ProtectedHistoryTurns = 0, MaxHistoryTurns = 0, UseProjectTools = false };
            var owner = Conversation.Create("A", "local", ""); var other = Conversation.Create("B", "local", "");
            db.Conversations.AddRange(owner, other);
            void Turn(Conversation chat, int day, string user, string assistant)
            {
                var id = Guid.NewGuid(); var time = new DateTimeOffset(2026, 1, day, 12, 0, 0, TimeSpan.Zero);
                db.Messages.Add(ChatMessage.Import(chat.Id, chat.MessageCount + 1, id, "user", user, time));
                db.Messages.Add(ChatMessage.Import(chat.Id, chat.MessageCount + 2, id, "assistant", assistant, time.AddSeconds(1)));
                chat.CompleteTurn();
            }
            Turn(owner, 1, "Décision [région]: Normandie", "Proposition [région]: Bourgogne");
            Turn(owner, 4, "Correction [région]: Bretagne", "Merci.");
            Turn(other, 2, "Décision [région]: INTERDIT", "D'accord.");
            await db.SaveChangesAsync();
            var store = new TemporalMemoryStore(new Alycia.Modules.Memory.TemporalFactStorage(db.Database), new TemporalHistorySources(db, options), new NoVectors());
            await store.RefreshAsync(default); await store.RefreshAsync(default);
            var data = await store.ExportAsync(owner.Id, 0, 50, default);
            Assert.Equal(4, data.Length); Assert.DoesNotContain(data, f => f.Quote.Contains("INTERDIT"));
            Assert.Equal("proposition", data.Single(f => f.Sequence == 2).Kind);
            async Task<ContextWindow> Recall(string question)
            {
                var copy = options.Copy();
                copy.Recall = await store.RecallAsync(owner.Id, question, TemporalRecallSettings.Capture(copy), owner.MessageCount, default);
                return (await TemporalContext.BuildAsync(new EfChatRepository(db, ChatFixture.Profiles(db), new TurnDocuments(db, ChatFixture.DocumentScope)), owner.ForContext(copy), question, copy.Context(), default)).Context;
            }
            var current = await Recall("Quelle région ?");
            Assert.Contains(current.MemoryTexts!, t => t.Text.Contains("Bretagne"));
            Assert.DoesNotContain(current.MemoryTexts!, t => t.Text.Contains("Normandie"));
            var past = await Recall("Quelle région au 2026-01-02 ?");
            Assert.Contains(past.MemoryTexts!, t => t.Text.Contains("Normandie"));
            Assert.DoesNotContain(past.MemoryTexts!, t => t.Text.Contains("Bretagne"));
            if (Environment.GetEnvironmentVariable("ALYCIA_TEST_PGVECTOR") == "1")
                await db.Database.ExecuteSqlRawAsync("CREATE EXTENSION vector");
            var embeddings = ChatFixture.Embeddings(db);
            foreach (var fact in data.Concat(await store.ExportAsync(other.Id, 0, 10, default)))
            {
                var scope = fact.Quote.Contains("INTERDIT") ? other.Id : owner.Id;
                if (fact.Role == "assistant") continue;
                var chunk = new SourceChunk(Guid.NewGuid(), "history/" + fact.Sequence, fact.Sha256, fact.Role, "history",
                    0, fact.Quote.Length, 1, 1, fact.Quote, [], true);
                await embeddings.WriteAsync("fixture:digest", chunk, [1, 0], scope, fact.Sequence, default);
            }
            var semanticOptions = options.Copy(); semanticOptions.RetrievalMode = "hybrid"; semanticOptions.EmbeddingModel = "fixture";
            var engine = new BackgroundEmbeddingIndex(ChatFixture.IndexFiles(db, new(new FileStorage(Path.GetTempPath()))), ChatFixture.LiveFiles(db), embeddings, new QueryEncoder(), options, new ConversationIndexOwners(new(db.Database)), ChatFixture.Profiles(db), ChatFixture.Profiles(db), ChatFixture.Foreground);
            var semanticStore = new TemporalMemoryStore(new Alycia.Modules.Memory.TemporalFactStorage(db.Database), new TemporalHistorySources(db, options), engine);
            var paraphrase = await semanticStore.RecallAsync(owner.Id, "Territoire géographique ?", TemporalRecallSettings.Capture(semanticOptions), 4, default);
            Assert.Single(paraphrase.Calls);
            Assert.Contains(paraphrase.Facts, f => f.Quote.Contains("Bretagne"));
            Assert.DoesNotContain(paraphrase.Facts, f => f.Quote.Contains("INTERDIT"));
            var ref1 = past.Temporal!.Sources.Single(s => s.Role == "user");
            Assert.Contains(ref1.Reference, CitationAudit.Check($"Preuve [{ref1.Reference}]", [], [], past.Temporal.Sources).Recognized);
            await store.ClearAsync(owner.Id, default);
            Assert.Empty(await store.ExportAsync(owner.Id, 0, 50, default));
            Assert.Equal(4, await db.Messages.CountAsync(m => m.ConversationId == owner.Id));
            await store.RefreshAsync(default);
            Assert.Equal(data, await store.ExportAsync(owner.Id, 0, 50, default));
            db.Conversations.Remove(owner); await db.SaveChangesAsync();
            Assert.Empty(await store.ExportAsync(owner.Id, 0, 50, default));
            Assert.Equal(2, (await store.ExportAsync(other.Id, 0, 50, default)).Length);
        }
        finally
        {
            await using var drop = new NpgsqlCommand($"DROP DATABASE {name} WITH (FORCE)", admin);
            await drop.ExecuteNonQueryAsync();
        }
    }
    private sealed class QueryEncoder : ILocalEmbeddings
    {
        public Task<string> IdentityAsync(string model, CancellationToken ct) => Task.FromResult("fixture:digest");
        public Task<EmbeddingBatch> EmbedAsync(string model, IReadOnlyList<string> input, CancellationToken ct) =>
            Task.FromResult(new EmbeddingBatch(input.Select(_ => new float[] { 1, 0 }).ToArray(), null));
    }
    private sealed class NoVectors : IIndexedEmbeddings
    {
        public Task<SemanticSearch> SearchAsync(Guid conversation, string query, EmbeddingQuery settings, IReadOnlyList<SourceChunk> chunks, CancellationToken ct) =>
            throw new InvalidOperationException("Le mode lexical ne demande pas de vecteurs.");
    }
}
