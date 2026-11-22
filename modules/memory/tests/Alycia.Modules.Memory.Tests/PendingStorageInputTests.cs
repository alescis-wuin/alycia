using Alycia.Modules.Memory;

namespace Alycia.Modules.Chat.Tests;

public sealed class PendingStorageInputTests
{
    [PostgresFact]
    public Task Queued_fact_write_and_lookup_keep_the_supplied_topics() =>
        MemoryTestDatabase.WithSingleConnection(async (db, gate, owner) =>
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
}
