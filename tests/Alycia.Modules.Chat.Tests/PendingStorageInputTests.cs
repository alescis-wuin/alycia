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
            var passage = new SourceChunk(owner, "memory", "hash", "", "message", 0, 5, 1, 1, "Paris", [], true);
            float[] vector = [1, 0];
            var pending = store.WriteAsync("fixture", passage, vector, owner, 1, default);
            Assert.False(pending.IsCompleted);
            vector[0] = 0;
            vector[1] = 1;
            await gate.CloseAsync();
            await pending;
            var key = EmbeddingChunks.Key(passage);
            var stored = await store.ReadAsync("fixture", [key], default);
            Assert.Equal([1f, 0f], stored[key]);
        });

    [PostgresFact]
    public Task Array_fallback_uses_the_submitted_keys_and_query_with_one_connection() =>
        WithSingleConnection(async (db, gate, owner) =>
        {
            var store = ChatFixture.Embeddings(db);
            var passage = new SourceChunk(owner, "memory", "hash", "", "message", 0, 5, 1, 1, "Paris", [], true);
            var key = EmbeddingChunks.Key(passage);
            await gate.CloseAsync();
            await store.WriteAsync("fixture", passage, [1, 0], owner, 1, default);
            await gate.OpenAsync();
            string[] keys = [key];
            float[] query = [1, 0];
            var pending = store.SimilaritiesAsync("fixture", keys, query, default);
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

    [PostgresFact]
    public Task Queued_fact_write_and_lookup_keep_the_supplied_topics() =>
        WithSingleConnection(async (db, gate, owner) =>
        {
            var facts = new TemporalFactStorage(db.Database);
            string[] topics = ["Paris"];
            var before = DateTimeOffset.UtcNow;
            var pending = facts.SaveAsync(owner, 1, before.AddMinutes(-1), topics, "{}", "Paris", default);
            Assert.False(pending.IsCompleted);
            topics[0] = "Lyon";
            await gate.CloseAsync();
            await pending;
            await gate.OpenAsync();
            string[] filter = ["Paris"];
            var lookup = facts.TopicsAsync(owner, 1, before, filter, default);
            Assert.False(lookup.IsCompleted);
            filter[0] = "Rouen";
            await gate.CloseAsync();
            Assert.Equal("{}", Assert.Single(await lookup));
        });

    private static Task WithSingleConnection(Func<ChatDbContext, NpgsqlConnection, Guid, Task> run) =>
        ProjectCreationTests.WithDatabaseAsync(async (options, _) =>
        {
            await using var seed = new ChatDbContext(options);
            var owner = Conversation.Create("test", "local", "");
            seed.Add(owner);
            await seed.SaveChangesAsync();
            var connection = new NpgsqlConnectionStringBuilder(seed.Database.GetConnectionString())
            {
                Pooling = true, MaxPoolSize = 1, Timeout = 4, ApplicationName = "pending-input"
            }.ToString();
            await using var gate = new NpgsqlConnection(connection);
            await gate.OpenAsync();
            await using var db = new ChatDbContext(new DbContextOptionsBuilder<ChatDbContext>().UseNpgsql(connection).Options);
            try { await run(db, gate, owner.Id); }
            finally { NpgsqlConnection.ClearPool(gate); }
        });
}
