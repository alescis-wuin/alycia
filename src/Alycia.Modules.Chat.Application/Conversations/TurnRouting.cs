using System.Collections.Immutable;
using Alycia.Contracts.Memory;
using Alycia.Contracts.Retrieval;

namespace Alycia.Modules.Chat.Application;

// The coordinator sequences read-only planning, optional historical recall, then retrieval.
// Each dependency receives only the settings and owner scope needed for its own operation.
public sealed class TurnRouting(IQueryPlanner planner, IProjectRetrieval? retrieval = null, ITemporalMemory? memory = null) : ITurnRouter
{
    public async Task<RoutedTurn> PrepareAsync(Guid owner, int messageCount, string question, RoutingRequest request,
        IQueryBriefRefiner refiner, Func<Task>? reportPreparation, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (retrieval is not null && reportPreparation is not null) await reportPreparation();
        var prior = request.Initial;
        if (retrieval is null && !request.NeedsSources) return new(prior, []);
        var scope = await planner.PlanAsync(owner, messageCount, question, request.Rules, prior.Query, refiner, ct);
        var calls = ImmutableArray.CreateBuilder<ModelCallUsage>();
        var recall = prior.Recall;
        if (request.Memory is { } settings && memory is not null && scope.Route?.Mode is not ("direct" or "unavailable"))
        {
            recall = await memory.RecallAsync(owner, question, settings, messageCount, ct);
            calls.AddRange(recall.Calls);
        }

        IReadOnlyList<SourceChunk>? chunks = prior.Chunks;
        var report = prior.Report;
        if (scope.Route?.Search == false) chunks = [];
        else if (retrieval is not null && request.Search.Passages.Mode != "legacy")
        {
            var passages = request.Search.Passages;
            var scoped = new RetrievalRequest(passages.Mode, scope.Eligible, passages.Ranking with { PinnedSourceIds = scope.Pinned },
                passages.ExpandQueries, scope.Plan);
            var sources = await retrieval.PrepareAsync(owner, question, request.Search with { Passages = scoped }, ct);
            chunks = sources.Chunks;
            report = sources.Report;
            calls.AddRange(sources.Calls);
        }
        return new(new(scope, recall, chunks, report), calls.ToImmutable());
    }
}
