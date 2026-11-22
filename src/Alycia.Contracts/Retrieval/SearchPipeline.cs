using System.Collections.Frozen;
using System.Collections.Immutable;
using Alycia.Contracts.Chat;
using Alycia.Contracts.Indexing;

namespace Alycia.Contracts.Retrieval;

public sealed record EvidenceText(string Reference, string Text, string? Role = null);
public sealed class RetrievalPreparation(IReadOnlyList<SourceChunk> chunks, RetrievalReport report, IReadOnlyList<ModelCallUsage> calls)
{
    private readonly RetrievalReport _report = SearchSnapshots.Copy(report)!;
    public IReadOnlyList<SourceChunk> Chunks { get; } = chunks.ToImmutableArray();
    public RetrievalReport Report => SearchSnapshots.Copy(_report)!;
    public IReadOnlyList<ModelCallUsage> Calls { get; } = calls.ToImmutableArray();
}

public sealed class SemanticSearch(double[] scores, int hits, IReadOnlyList<ModelCallUsage> calls, string? warning = null)
{
    private readonly double[] _scores = scores.ToArray();
    public double[] Scores => _scores.ToArray();
    public int Hits { get; } = hits;
    public IReadOnlyList<ModelCallUsage> Calls { get; } = calls.ToImmutableArray();
    public string? Warning { get; } = warning;
}

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
