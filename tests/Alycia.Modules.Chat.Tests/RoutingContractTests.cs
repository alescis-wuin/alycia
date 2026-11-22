using System.Collections.Frozen;
using Alycia.Contracts.Documents;
using Alycia.Contracts.Indexing;
using Alycia.Contracts.Retrieval;
using Alycia.Modules.Chat.Application;
using Alycia.Modules.Retrieval;
using Alycia.Modules.Testing;

namespace Alycia.Modules.Chat.Tests;

public sealed class RoutingContractTests
{
    [Fact]
    public async Task Planning_and_retrieval_respect_the_same_owners_pins_and_semantic_callback_scope()
    {
        var owner = Guid.NewGuid(); var other = Guid.NewGuid();
        TurnFile File(string name, string mode) => new(Guid.NewGuid(), null, owner, Guid.Empty, null, name, 1, "sha", "text", "orion", 1, default, "", "", "", mode);
        var a = File("orion.md", "pinned"); var b = File("private.md", "excluded");
        (Guid Owner, TurnFile File)[] rows = [(owner, a), (owner, b)];
        SourceChunk Chunk(TurnFile f) => new(f.Id, f.Name, f.Sha256, f.Name, "text", 0, 5, 1, 1, "orion", ["source"], true);
        var index = new SnapshotIndex(owner, [Chunk(a), Chunk(b)], other);
        var rank = new PassageRankingSettings(false, false, 4, 1, .6, .3, false, 16, 100, null);
        var policy = new RetrievalRequest("hybrid", new[] { a.Id }.ToFrozenSet(), rank, false, null);
        var search = new ProjectSearch(policy, new("test", 1, 200, false, "", ""));
        var pipeline = new RetrievalPipeline(index, new());
        var fixture = new FixtureRetrieval(index);
        foreach (IRetrievalPipeline adapter in new IRetrievalPipeline[] { fixture, pipeline })
        {
            var seen = 0;
            var result = await adapter.PrepareAsync(owner, "orion", policy, (q, chunks, ct) =>
            { Assert.Equal(a.Id, Assert.Single(chunks).FileId); seen++; return Task.FromResult(new SemanticSearch([1], 1, [])); }, default);
            Assert.Equal(1, seen);
            Assert.Equal(a.Id, Assert.Single(result.Chunks).FileId);
            result.Chunks[0].References[0] = "changed";
            Assert.Equal("source", result.Chunks[0].References[0]);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => adapter.PrepareAsync(owner, "", policy, null, new(true)));
        }
        var project = new ProjectRetrievalService(pipeline);
        foreach (var adapter in new IProjectRetrieval[] { fixture, project })
        {
            Assert.Empty((await adapter.PrepareAsync(other, "orion", search, default)).Chunks);
            Assert.Equal(a.Id, Assert.Single((await adapter.PrepareAsync(owner, "orion", search, default)).Chunks).FileId);
            await Assert.ThrowsAsync<ConversationNotFoundException>(() => adapter.PrepareAsync(Guid.Empty, "orion", search, default));
        }
        var simulated = new FixtureQueryPlanner(rows, q => q == "merci" ? new("direct", "fixture", false, false) : new("retrieval", "fixture", true, true));
        var real = new QueryPlanner(new SnapshotTurnReads([owner, other], [], rows));
        var prior = new PlannedQuery(true, null, null, null, null);
        var rules = new RoutingRules("auto", 2, 1, false, false, null);
        var refiner = new RecordedQueryBrief(_ => null, "fixture");
        foreach (var planner in new IQueryPlanner[] { simulated, real })
        {
            var scoped = await planner.PlanAsync(owner, 1, "lis orion.md", rules, prior, refiner, default);
            Assert.Equal(a.Id, Assert.Single(scoped.Eligible!)); Assert.Equal(a.Id, Assert.Single(scoped.Pinned!));
            Assert.True(scoped.Route!.Search);
            Assert.False((await planner.PlanAsync(other, 1, "merci", rules, prior, refiner, default)).UseTools);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => planner.PlanAsync(other, 0, "", rules, prior, refiner, new(true)));
        }
        var request = new RoutingRequest(rules, true, null, search, new(prior, null, null, null));
        foreach (var router in new ITurnRouter[] { new FixtureTurnRouter(simulated, fixture), new TurnRouting(real, project) })
        {
            var progress = 0;
            var result = await router.PrepareAsync(owner, 1, "lis orion.md", request, refiner, () => { progress++; return Task.CompletedTask; }, default);
            Assert.Equal(1, progress); Assert.Equal(a.Id, Assert.Single(result.State.Chunks!).FileId);
            Assert.Empty((await router.PrepareAsync(other, 0, "merci", request, refiner, null, default)).State.Chunks!);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => router.PrepareAsync(owner, 0, "", request, refiner, null, new(true)));
        }
    }
}
