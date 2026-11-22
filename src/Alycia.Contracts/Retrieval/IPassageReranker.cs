using Alycia.Contracts.Indexing;

namespace Alycia.Contracts.Retrieval;

public sealed record RerankScores(string Identity, double[] Values);
public interface IPassageReranker
{
    Task<RerankScores> ScoreAsync(string question, IReadOnlyList<SourceChunk> passages, CancellationToken ct);
}
