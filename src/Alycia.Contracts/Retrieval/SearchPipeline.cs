using System.Collections.Frozen;
using Alycia.Contracts.Chat;
using Alycia.Contracts.Indexing;

namespace Alycia.Contracts.Retrieval;

public sealed record EvidenceText(string Reference, string Text, string? Role = null);
public sealed record RetrievalPreparation(IReadOnlyList<SourceChunk> Chunks, RetrievalReport Report, IReadOnlyList<ModelCallUsage> Calls);
public sealed record SemanticSearch(double[] Scores, int Hits, IReadOnlyList<ModelCallUsage> Calls, string? Warning = null);

public sealed class RetrievalRequest(string mode, FrozenSet<Guid>? eligibleSourceIds, PassageRankingSettings ranking,
    bool expandQueries, SearchQueryPlan? plan)
{
    private readonly SearchQueryPlan? _plan = SearchSnapshots.Copy(plan);
    public string Mode { get; } = mode;
    public FrozenSet<Guid>? EligibleSourceIds { get; } = eligibleSourceIds;
    public PassageRankingSettings Ranking { get; } = ranking;
    public bool ExpandQueries { get; } = expandQueries;
    public SearchQueryPlan? Plan => SearchSnapshots.Copy(_plan);
}

public delegate Task<SemanticSearch> PassageSimilarities(string query, IReadOnlyList<SourceChunk> chunks, CancellationToken ct);
public interface IRetrievalPipeline
{
    Task<RetrievalPreparation> PrepareAsync(Guid owner, string query, RetrievalRequest request, PassageSimilarities? semantic, CancellationToken ct);
}
