using System.Collections.Frozen;
using System.Collections.Immutable;
using Alycia.Contracts.Context;
using Alycia.Contracts.Chat;
using Alycia.Contracts.Retrieval;

namespace Alycia.Contracts.Indexing;

public sealed class MemoryVectorMatches(IReadOnlyDictionary<int, double> scores, IReadOnlyList<ModelCallUsage> calls, string? warning = null)
{
    public IReadOnlyDictionary<int, double> Scores { get; } = scores.ToFrozenDictionary();
    public IReadOnlyList<ModelCallUsage> Calls { get; } = calls.ToImmutableArray();
    public string? Warning { get; } = warning;
}
public interface IIndexedEmbeddings
{
    Task<SemanticSearch> SearchAsync(Guid conversation, string query, EmbeddingQuery settings,
        IReadOnlyList<SourceChunk> chunks, CancellationToken ct);
    Task<MemoryVectorMatches> SearchMemoryAsync(Guid owner, string question, EmbeddingQuery settings, int through, DateTimeOffset before, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(new MemoryVectorMatches(new Dictionary<int, double>(), [], "Index vectoriel historique indisponible."));
    }

    Task<SemanticSearch> SearchHistoryAsync(Guid conversation, string query, EmbeddingQuery settings,
        IReadOnlyList<HistoryMessage> messages, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(new SemanticSearch(new double[messages.Count], 0, []));
    }
}
