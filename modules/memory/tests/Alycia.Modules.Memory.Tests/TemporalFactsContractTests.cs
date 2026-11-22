using System.Text.Json;
using Alycia.Contracts.Memory;
using Alycia.Modules.Testing;
using Microsoft.EntityFrameworkCore;
using Alycia.Modules.Memory;

namespace Alycia.Modules.Chat.Tests;

public sealed class TemporalFactsContractTests
{
    [PostgresFact]
    public Task Fact_storage_preserves_first_observation_orders_exports_and_bounds_search_by_owner_and_date() =>
        MemoryTestDatabase.WithDatabaseAsync(async db =>
        {
            var owner = Guid.NewGuid();
            var foreign = Guid.NewGuid();
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO chat.conversations VALUES ({owner}), ({foreign})");
            var date = DateTimeOffset.UnixEpoch.AddDays(90);
            static int[] Values(IEnumerable<string> entries) => entries.Select(s => JsonSerializer.Deserialize<int[]>(s)![0]).ToArray();
            foreach (var provider in new ITemporalFactStorage[] { new TemporalFactStorage(db.Database), new MemoryTemporalFacts([owner, foreign]) })
            {
                var topics = new[] { "moteur" };
                await provider.SaveAsync(owner, 2, date.AddDays(1), topics, "[2]", "sucre", default);
                topics[0] = "muté";
                Assert.Equal(0, await provider.ContiguousThroughAsync(owner, 2, default));
                await provider.SaveAsync(owner, 1, date, ["moteur"], "[1]", "turbo", default);
                await provider.SaveAsync(owner, 1, date, ["absent"], "[9]", "faux", default);
                await provider.SaveAsync(foreign, 1, date, ["moteur"], "[8]", "turbo", default);
                Assert.Equal(2, await provider.ContiguousThroughAsync(owner, 4, default));
                Assert.Equal(1, await provider.CountAsync(owner, 1, default));
                Assert.Equal([2], (await provider.IndexedAsync(owner, [2, 3], default)).AsEnumerable());
                Assert.Equal([1, 2], Values(await provider.TopicsAsync(owner, 4, date.AddDays(2), ["moteur"], default)).AsEnumerable());
                Assert.Equal([1], Values(await provider.SelectedAsync(owner, 4, date.AddDays(1), [1, 2], default)).AsEnumerable());
                var ranked = Values(await provider.LexicalAsync(owner, 4, date.AddDays(2), "turbo", default));
                Assert.Equal(1, ranked[0]);
                Assert.DoesNotContain(8, ranked);
                Assert.Equal([2], Values(await provider.ExportAsync(owner, 1, 1, default)).AsEnumerable());
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => provider.ClearAsync(owner, new(true)));
                Assert.Equal(2, await provider.ClearAsync(owner, default));
                Assert.Equal(0, await provider.ClearAsync(owner, default));
                Assert.Equal(1, await provider.CountAsync(foreign, 4, default));
            }
        });
}
