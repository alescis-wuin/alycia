using Alycia.Modules.Chat.Application;

namespace Alycia.Modules.Chat.Infrastructure;

public sealed class IndexMaintenance(ITemporalMemoryIndex memory, BackgroundEmbeddingIndex index, PostgresEmbeddingStore store)
    : IIndexMaintenance
{
    public async Task RunCycleAsync(CancellationToken ct)
    {
        await memory.RefreshAsync(ct);
        await index.DiscoverAsync(ct);
        foreach (var job in await store.JobsAsync(ct))
            await index.ProcessAsync(job, ct);
    }
}
