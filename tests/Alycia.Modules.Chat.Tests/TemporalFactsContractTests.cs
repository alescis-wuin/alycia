using System.Text.Json;
using Alycia.Contracts.Memory;
using Alycia.Modules.Chat.Domain;
using Alycia.Modules.Memory;
using Alycia.Modules.Testing;

namespace Alycia.Modules.Chat.Tests;

public sealed class TemporalFactsContractTests
{
    [PostgresFact]
    public Task Fact_storage_preserves_first_observation_orders_exports_and_bounds_search_by_owner_and_date() =>
        ProjectCreationTests.WithDatabaseAsync(async (options, _) =>
        {
            await using var db = new ChatDbContext(options);
            var owner = Conversation.Create("A", "local", "");
            var foreign = Conversation.Create("B", "local", "");
            db.AddRange(owner, foreign);
            await db.SaveChangesAsync();
            var date = DateTimeOffset.UnixEpoch.AddDays(90);
            static int[] Values(IEnumerable<string> entries) => entries.Select(s => JsonSerializer.Deserialize<int[]>(s)![0]).ToArray();
            foreach (var provider in new ITemporalFactStorage[] { new TemporalFactStorage(db.Database), new MemoryTemporalFacts([owner.Id, foreign.Id]) })
            {
                var topics = new[] { "moteur" };
                await provider.SaveAsync(owner.Id, 2, date.AddDays(1), topics, "[2]", "sucre", default);
                topics[0] = "muté";
                Assert.Equal(0, await provider.ContiguousThroughAsync(owner.Id, 2, default));
                await provider.SaveAsync(owner.Id, 1, date, ["moteur"], "[1]", "turbo", default);
                await provider.SaveAsync(owner.Id, 1, date, ["absent"], "[9]", "faux", default);
                await provider.SaveAsync(foreign.Id, 1, date, ["moteur"], "[8]", "turbo", default);
                Assert.Equal(2, await provider.ContiguousThroughAsync(owner.Id, 4, default));
                Assert.Equal(1, await provider.CountAsync(owner.Id, 1, default));
                Assert.Equal([2], (await provider.IndexedAsync(owner.Id, [2, 3], default)).AsEnumerable());
                Assert.Equal([1, 2], Values(await provider.TopicsAsync(owner.Id, 4, date.AddDays(2), ["moteur"], default)).AsEnumerable());
                Assert.Equal([1], Values(await provider.SelectedAsync(owner.Id, 4, date.AddDays(1), [1, 2], default)).AsEnumerable());
                var ranked = Values(await provider.LexicalAsync(owner.Id, 4, date.AddDays(2), "turbo", default));
                Assert.Equal(1, ranked[0]);
                Assert.DoesNotContain(8, ranked);
                Assert.Equal([2], Values(await provider.ExportAsync(owner.Id, 1, 1, default)).AsEnumerable());
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => provider.ClearAsync(owner.Id, new(true)));
                Assert.Equal(2, await provider.ClearAsync(owner.Id, default));
                Assert.Equal(0, await provider.ClearAsync(owner.Id, default));
                Assert.Equal(1, await provider.CountAsync(foreign.Id, 4, default));
            }
        });
}
