using Alycia.Contracts.Memory;
using System.Collections.Frozen;
using Alycia.Contracts.Documents;

namespace Alycia.Modules.Testing;

// The fixture ranks literal hits and keeps the same authorization boundary as the production pipeline.
public sealed class FixtureRetrieval(IProjectIndex index, IIndexedEmbeddings? vectors = null) : IRetrievalPipeline, IProjectRetrieval
{
    public Task<RetrievalPreparation> PrepareAsync(Guid owner, string query, ProjectSearch request, CancellationToken ct) =>
        PrepareAsync(owner, query, request.Passages, vectors is null ? null :
            (text, chunks, token) => vectors.SearchAsync(owner, text, request.Embeddings, chunks, token), ct);

    public async Task<RetrievalPreparation> PrepareAsync(Guid owner, string query, RetrievalRequest request, PassageSimilarities? semantic, CancellationToken ct)
    {
        var scope = await index.BuildScopedAsync(owner, request.EligibleSourceIds, ct);
        var score = request.Mode == "hybrid" && scope.Chunks.Count != 0 && semantic is not null
            ? await semantic(query, scope.Chunks, ct) : null;
        var ranked = scope.Chunks.Select((chunk, i) => (chunk, score: (chunk.Text.Contains(query, StringComparison.OrdinalIgnoreCase) ? 1 : 0)
            + (score?.Scores[i] ?? 0))).OrderByDescending(x => x.score).Select(x => x.chunk).ToArray();
        return new(ranked, new(request.Mode, scope.Chunks.Count, scope.IndexedFiles, scope.EligibleFiles, scope.Omissions), score?.Calls ?? []);
    }
}

public sealed class FixtureQueryPlanner(IEnumerable<(Guid Owner, TurnFile File)> files, Func<string, QueryRoute> route) : IQueryPlanner
{
    private readonly ILookup<Guid, TurnFile> _files = files.ToLookup(x => x.Owner, x => x.File);
    public Task<PlannedQuery> PlanAsync(Guid owner, int messageCount, string question, RoutingRules rules,
        PlannedQuery initial, IQueryBriefRefiner refiner, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var selected = _files[owner].Where(f => f.InContextWindow(Math.Max(1, messageCount - rules.HistoryMessages + 1), messageCount + 1)).ToArray();
        var decision = rules.Mode == "off" ? initial.Route : route(question);
        return Task.FromResult(new PlannedQuery(initial.UseTools && decision?.Tools != false, decision, selected.Select(f => f.Id).ToFrozenSet(),
            selected.Where(f => f.ContextMode == "pinned").Select(f => f.Id).ToFrozenSet(), initial.Plan));
    }
}

public sealed class FixtureTurnRouter(IQueryPlanner planner, IProjectRetrieval? retrieval = null, ITemporalMemory? memory = null) : ITurnRouter
{
    public async Task<RoutedTurn> PrepareAsync(Guid owner, int messageCount, string question, RoutingRequest request,
        IQueryBriefRefiner refiner, Func<Task>? progress, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (retrieval is not null && progress is not null) await progress();
        if (retrieval is null && !request.NeedsSources) return new(request.Initial, []);
        var query = await planner.PlanAsync(owner, messageCount, question, request.Rules, request.Initial.Query, refiner, ct);
        var recall = request.Initial.Recall;
        if (memory is not null && request.Memory is { } settings && query.Route?.Mode is not ("direct" or "unavailable"))
            recall = await memory.RecallAsync(owner, question, settings, messageCount, ct);
        var calls = recall is null || ReferenceEquals(recall, request.Initial.Recall) ? [] : recall.Calls;
        if (query.Route?.Search == false) return new(new(query, recall, [], request.Initial.Report), calls);
        if (retrieval is null || request.Search.Passages.Mode == "legacy") return new(new(query, recall, request.Initial.Chunks, request.Initial.Report), calls);
        var policy = request.Search.Passages;
        var retrieved = await retrieval.PrepareAsync(owner, question, request.Search with
        {
            Passages = new(policy.Mode, query.Eligible,
            policy.Ranking with { PinnedSourceIds = query.Pinned }, policy.ExpandQueries, query.Plan)
        }, ct);
        return new(new(query, recall, retrieved.Chunks, retrieved.Report), [.. calls, .. retrieved.Calls]);
    }
}
