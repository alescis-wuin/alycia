using System.Collections.Immutable;
using Alycia.Contracts.Chat;
using Alycia.Contracts.Indexing;

namespace Alycia.Contracts.Memory;

public sealed record TemporalFact(string Key, string Quote, string Role, int Sequence, DateTimeOffset Observed,
    string Sha256, int Offset, int FullLength, bool Correction, string Kind);

public sealed class TemporalRecall(IEnumerable<TemporalFact> facts, int indexed, int total,
    IEnumerable<ModelCallUsage> calls, string? warning = null)
{
    public ImmutableArray<TemporalFact> Facts { get; } = [.. facts];
    public int Indexed { get; } = indexed;
    public int Total { get; } = total;
    public ImmutableArray<ModelCallUsage> Calls { get; } = [.. calls];
    public string? Warning { get; } = warning;
}

public sealed record TemporalRecallSettings(bool SearchVectors, string? KnowledgeProfile, EmbeddingQuery Embeddings)
{
    public static TemporalRecallSettings Capture(IIndexSettings source) => new(
        source.RetrievalMode == "hybrid" && source.EmbedHistory && source.EmbeddingModel.Length > 0,
        source.EmbedHistory && source.Enrichment.Enabled ? source.Enrichment.Profile : null,
        EmbeddingQuery.Capture(source));
}

public interface ITemporalMemory
{
    Task<TemporalRecall> RecallAsync(Guid owner, string question, TemporalRecallSettings settings, int through, CancellationToken ct);
}
