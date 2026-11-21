using Alycia.Contracts.Chat;
using Alycia.Contracts.Documents;
using Alycia.Contracts.Retrieval;

namespace Alycia.Modules.Retrieval.Tests;

public sealed class QueryPlannerTests
{
    private sealed class DelayedHistory : IQuerySources, IQueryBriefRefiner
    {
        public TaskCompletionSource<IReadOnlyList<TurnFile>> SourceRead { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<IReadOnlyList<TurnFile>> ContextFilesAsync(Guid owner, int first, CancellationToken ct) =>
            SourceRead.Task.WaitAsync(ct);
        public Task<IReadOnlyList<QueryHistoryItem>> RecentForQueryAsync(Guid owner, int limit, CancellationToken ct) =>
            throw new InvalidOperationException("Aucun historique à réécrire.");
        public Task<QueryBrief> RefineAsync(QueryBrief brief, BriefGeneration settings, CancellationToken ct) =>
            throw new InvalidOperationException("Aucun appel d’inférence attendu.");
    }

    [Fact]
    public async Task ScopeAndPlanAreStableWhenTheSourceReadIsSuspended()
    {
        var owner = Guid.NewGuid();
        TurnFile File(string name, string mode, int anchor) => new(Guid.NewGuid(), null, owner, Guid.Empty, null,
            name, 1, "sha", "text", "x", anchor, default, "", "", "unknown", mode);
        var visible = File("visible.md", "pinned", 1);
        var plan = new SearchQueryPlan("initial", [new("plan initial", "lexical")], [], false);
        var prior = new PlannedQuery(true, null, null, null, plan);
        var rules = new RoutingRules("auto", 2, 3, false, false, null);
        var reader = new DelayedHistory();
        var pending = new QueryPlanner(reader).PlanAsync(owner, 6, "Lis visible.md", rules, prior, reader, default);

        plan.Queries[0] = new("altéré par l’appelant", "semantic");
        reader.SourceRead.SetResult([visible, File("futur", "pinned", 8), File("exclu", "excluded", 6), File("ancien", "recent", 1)]);
        var routed = await pending;

        Assert.Equal(visible.Id, Assert.Single(routed.Eligible!));
        Assert.Equal(visible.Id, Assert.Single(routed.Pinned!));
        Assert.True(routed.Route!.Search);
        Assert.Equal("plan initial", routed.Plan!.Queries[0].Text);
        routed.Plan.Queries[0] = new("altéré par le consommateur", "semantic");
        Assert.Equal("plan initial", routed.Plan.Queries[0].Text);
        Assert.Null(prior.Eligible);
    }
}
