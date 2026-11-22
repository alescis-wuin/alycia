using Alycia.Contracts.Context;
using Alycia.Contracts.Chat;
using Alycia.Contracts.Retrieval;

namespace Alycia.Contracts.Indexing;

public sealed record MemoryVectorMatches(IReadOnlyDictionary<int, double> Scores, IReadOnlyList<ModelCallUsage> Calls, string? Warning = null);
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
