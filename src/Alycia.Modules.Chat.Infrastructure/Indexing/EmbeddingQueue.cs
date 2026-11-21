using Alycia.Modules.Chat.Application;

namespace Alycia.Modules.Chat.Infrastructure;

public static class EmbeddingQueue
{
    public static Task QueueAsync(this PostgresEmbeddingStore store, Guid owner, bool project, IndexParameters options,
        CancellationToken ct, bool retry = false, string sourceRevision = "")
    {
        options.Validate();
        if (string.IsNullOrWhiteSpace(options.EmbeddingModel) && !options.Enrichment.Enabled) return Task.CompletedTask;
        var profile = EmbeddingSettings.From(options) with { SourceRevision = sourceRevision };
        return store.QueueAsync(owner, project, profile, ct, retry);
    }
}
