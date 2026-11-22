using Alycia.Modules.Chat.Domain;
using Alycia.Modules.Chat.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Alycia.Modules.Chat.Tests;

public sealed class IndexPublicationTests
{
    [PostgresFact]
    public Task Temporal_vector_pages_keep_the_top_32_and_reject_wrong_hashes_dates_and_owners() =>
        ProjectCreationTests.WithDatabaseAsync(async (options, _) =>
        {
            await using var db = new ChatDbContext(options);
            var owner = Conversation.Create("Historique", "local", "");
            var outsider = Conversation.Create("Voisin", "local", "");
            db.AddRange(owner, outsider);
            await db.SaveChangesAsync();
            var before = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO chat.temporal_memory(owner,sequence,observed,topics,facts,search,version)
                SELECT {owner.Id}, n, CASE WHEN n=130 THEN {before} ELSE {before.AddDays(-1)} END, ARRAY[]::text[],
                    jsonb_build_array(jsonb_build_object('sha256','source')), ''::tsvector, 1
                FROM generate_series(1,130) n
                """);
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO chat.embedding_vectors(identity,key,conversation_id,sequence,start_offset,end_offset,content_hash,embedding)
                SELECT 'model', n::text, {owner.Id}, n, 0, 1, CASE WHEN n=129 THEN 'stale' ELSE 'source' END,
                    ARRAY[1,0]::real[] FROM generate_series(1,130) n
                """);
            var store = ChatFixture.Embeddings(db);
            async Task AssertRanks()
            {
                var selected = await store.MemorySimilaritiesAsync(owner.Id, "model", 130, before, [1, 0], default);
                Assert.Equal(Enumerable.Range(97, 32).Reverse(), selected.Keys);
                Assert.All(selected.Values, value => Assert.Equal(1, value));
                Assert.Empty(await store.MemorySimilaritiesAsync(outsider.Id, "model", 130, before, [1, 0], default));
            }
            await AssertRanks();
            if (Environment.GetEnvironmentVariable("ALYCIA_TEST_PGVECTOR") == "1")
            {
                await db.Database.ExecuteSqlRawAsync("CREATE EXTENSION IF NOT EXISTS vector");
                await AssertRanks();
            }
        });
}
