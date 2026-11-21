using System.Collections.Frozen;
using Alycia.Contracts.Chat;
using Alycia.Contracts.Indexing;

namespace Alycia.Contracts.Retrieval;

public sealed record EvidenceText(string Reference, string Text, string? Role = null);
public sealed record RetrievalPreparation(IReadOnlyList<SourceChunk> Chunks, RetrievalReport Report, IReadOnlyList<ModelCallUsage> Calls);
public sealed record SemanticSearch(double[] Scores, int Hits, IReadOnlyList<ModelCallUsage> Calls, string? Warning = null);

public sealed record RetrievalRequest(string Mode, FrozenSet<Guid>? EligibleSourceIds, PassageRankingSettings Ranking,
    bool ExpandQueries, SearchQueryPlan? Plan);

public delegate Task<SemanticSearch> PassageSimilarities(string query, IReadOnlyList<SourceChunk> chunks, CancellationToken ct);
public interface IRetrievalPipeline
{
    Task<RetrievalPreparation> PrepareAsync(Guid owner, string query, RetrievalRequest request, PassageSimilarities? semantic, CancellationToken ct);
}
