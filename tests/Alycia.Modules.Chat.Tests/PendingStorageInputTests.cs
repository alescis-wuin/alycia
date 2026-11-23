using Alycia.Modules.Chat.Domain;
using Alycia.Modules.Chat.Infrastructure;
using Alycia.Modules.Memory;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Alycia.Modules.Chat.Tests;

public sealed class PendingStorageInputTests
{
    [PostgresFact]
    public Task Queued_vector_write_keeps_the_validated_coordinates() =>
        WithSingleConnection(async (db, gate, owner) =>
        {
            var store = ChatFixture.Embeddings(db);
            var passage = new SourceChunk(owner, $"{owner:N}/history/1", MemoryTransfer.Hash("Paris"), "user", "history", 0, 5, 1, 1, "Paris", [], true);
            await gate.CloseAsync();
            var job = await QueueAsync(store, owner);
            await gate.OpenAsync();
            float[] vector = [1, 0];
            var pending = store.PublishEligibleVectorsAsync(job, "fixture", [passage], [vector], owner, 1, default);
            Assert.False(pending.IsCompleted);
            vector[0] = 0;
            vector[1] = 1;
            await gate.CloseAsync();
            Assert.True(await pending);
            var key = EmbeddingChunks.Key(passage);
            var stored = await store.ReadAsync(owner, false, "fixture", [key], default);
            Assert.Equal([1f, 0f], stored[key]);
        });

    [PostgresFact]
    public Task Array_fallback_uses_the_submitted_keys_and_query_with_one_connection() =>
        WithSingleConnection(async (db, gate, owner) =>
        {
            var store = ChatFixture.Embeddings(db);
            var passage = new SourceChunk(owner, $"{owner:N}/history/1", MemoryTransfer.Hash("Paris"), "user", "history", 0, 5, 1, 1, "Paris", [], true);
            var key = EmbeddingChunks.Key(passage);
            await gate.CloseAsync();
            Assert.True(await store.PublishEligibleVectorsAsync(await QueueAsync(store, owner), "fixture", [passage], [[1, 0]], owner, 1, default));
            await gate.OpenAsync();
            string[] keys = [key];
            float[] query = [1, 0];
            var pending = store.SimilaritiesAsync(owner, "fixture", keys, query, default);
            Assert.False(pending.IsCompleted);
            keys[0] = "another-key";
            query[0] = 0;
            query[1] = 1;
            await gate.CloseAsync();
            var scores = await pending;
            var score = Assert.Single(scores);
            Assert.Equal(key, score.Key);
            Assert.Equal(1d, score.Value, 6);
        });

    private static async Task<EmbeddingJob> QueueAsync(PostgresEmbeddingStore store, Guid owner)
    {
        await store.QueueAsync(owner, false, new EmbeddingSettings("model", 8, 128, true), default);
        return Assert.Single(await store.JobsAsync(default));
    }

    private static Task WithSingleConnection(Func<ChatDbContext, NpgsqlConnection, Guid, Task> run) =>
        ProjectCreationTests.WithDatabaseAsync(async (options, _) =>
        {
            await using var seed = new ChatDbContext(options);
            var owner = Conversation.Create("test", "local", "");
            seed.Add(owner);
            seed.Messages.Add(new ChatMessage(owner.Id, 1, owner.Id, "user", "Paris"));
            await seed.SaveChangesAsync();
            var connection = new NpgsqlConnectionStringBuilder(seed.Database.GetConnectionString())
            {
                Pooling = true,
                MaxPoolSize = 1,
                Timeout = 4,
                ApplicationName = "pending-input"
            }.ToString();
            await using var gate = new NpgsqlConnection(connection);
            await gate.OpenAsync();
            await using var db = new ChatDbContext(new DbContextOptionsBuilder<ChatDbContext>().UseNpgsql(connection).Options);
            try { await run(db, gate, owner.Id); }
            finally { NpgsqlConnection.ClearPool(gate); }
        });
}
