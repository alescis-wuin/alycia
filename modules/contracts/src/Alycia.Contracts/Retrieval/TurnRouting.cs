using Alycia.Contracts.Documents;
using System.Collections.Frozen;
using System.Collections.Immutable;
using Alycia.Contracts.Chat;
using Alycia.Contracts.Indexing;
using Alycia.Contracts.Memory;

namespace Alycia.Contracts.Retrieval;

public sealed record BriefGeneration(string Model, int InputTokens, int OutputTokens, int TimeoutSeconds);

public interface IQueryBriefRefiner
{
    Task<QueryBrief> RefineAsync(QueryBrief brief, BriefGeneration settings, CancellationToken ct);
}

// Plans, diagnostic reports and chunk references have array-based transport formats.
// The turn owns its copies, including when a consumer requests a transport value.
public sealed class RoutingState(PlannedQuery query, TemporalRecall? recall, IReadOnlyList<SourceChunk>? chunks, RetrievalReport? report)
{
    private readonly SourceChunk[]? _chunks = SearchSnapshots.CopyChunks(chunks);
    private readonly RetrievalReport? _report = SearchSnapshots.Copy(report);
    public PlannedQuery Query { get; } = query;
    public TemporalRecall? Recall { get; } = recall;
    public SourceChunk[]? Chunks => SearchSnapshots.CopyChunks(_chunks);
    public RetrievalReport? Report => SearchSnapshots.Copy(_report);
}

public sealed record RoutingRules(string Mode, int HistoryMessages, int MaxSearchQueries, bool PlanQueries,
    bool Facets, BriefGeneration? Refiner);

public sealed record RoutingRequest(RoutingRules Rules, bool NeedsSources, TemporalRecallSettings? Memory,
    ProjectSearch Search, RoutingState Initial);

public sealed class PlannedQuery(bool useTools, QueryRoute? route, FrozenSet<Guid>? eligible, FrozenSet<Guid>? pinned,
    SearchQueryPlan? plan)
{
    private readonly SearchQueryPlan? _plan = SearchSnapshots.Copy(plan);
    public bool UseTools { get; } = useTools;
    public QueryRoute? Route { get; } = route;
    public FrozenSet<Guid>? Eligible { get; } = eligible;
    public FrozenSet<Guid>? Pinned { get; } = pinned;
    public SearchQueryPlan? Plan => SearchSnapshots.Copy(_plan);
}

public sealed record QueryHistoryItem(Guid ConversationId, int Sequence, string Role, string Content);

public interface IQuerySources
{
    Task<IReadOnlyList<TurnFile>> ContextFilesAsync(Guid owner, int first, CancellationToken ct);
    // Last limit messages (both roles), sorted by increasing sequence.
    Task<IReadOnlyList<QueryHistoryItem>> RecentForQueryAsync(Guid owner, int limit, CancellationToken ct);
}

public interface IQueryPlanner
{
    Task<PlannedQuery> PlanAsync(Guid owner, int messageCount, string question, RoutingRules rules,
        PlannedQuery initial, IQueryBriefRefiner refiner, CancellationToken ct);
}

public sealed record RoutedTurn(RoutingState State, ImmutableArray<ModelCallUsage> Calls);

public interface ITurnRouter
{
    Task<RoutedTurn> PrepareAsync(Guid owner, int messageCount, string question, RoutingRequest request,
        IQueryBriefRefiner refiner, Func<Task>? reportPreparation, CancellationToken ct);
}
