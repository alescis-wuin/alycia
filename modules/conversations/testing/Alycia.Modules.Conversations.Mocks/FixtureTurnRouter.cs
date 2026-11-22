using Alycia.Contracts.Memory;
using System.Collections.Frozen;
using Alycia.Contracts.Documents;

namespace Alycia.Modules.Testing;

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
