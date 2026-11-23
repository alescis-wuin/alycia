using Alycia.Contracts.Memory;
using System.Text.Json;
using Alycia.Modules.Chat.Application;

namespace Alycia.Modules.Chat.Tests;

public sealed class TurnSnapshotTests
{
    private sealed class PausedIndex(Task<ProjectIndexSnapshot> result) : IProjectIndex
    {
        public Task<ProjectIndexSnapshot> BuildAsync(Guid conversation, CancellationToken ct) => result.WaitAsync(ct);
    }

    [Fact]
    public async Task Caller_cannot_expand_a_pending_index_request()
    {
        var allowed = Guid.NewGuid();
        var other = Guid.NewGuid();
        var scope = new HashSet<Guid> { allowed };
        var ready = new TaskCompletionSource<ProjectIndexSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        IProjectIndex index = new PausedIndex(ready.Task);
        var pending = index.BuildScopedAsync(Guid.NewGuid(), scope, default);
        scope.Add(other);
        SourceChunk Chunk(Guid id) => new(id, "a", "hash", "", "text", 0, 1, 1, 1, "a", [], true);
        ready.SetResult(new([Chunk(allowed), Chunk(other)], 2, 2, 0, []));
        var snapshot = await pending;
        Assert.Equal(allowed, Assert.Single(snapshot.Chunks).FileId);
        Assert.Equal(1, snapshot.EligibleFiles);
    }

    [Fact]
    public void Copy_detaches_runtime_collections_schema_and_nested_search_provenance()
    {
        var id = Guid.NewGuid();
        var scope = new HashSet<Guid> { id };
        string[] labels = ["initial"];
        string[] references = ["S1"];
        QueryHit[] hits = [new(0, 1, 1, null)];
        Dictionary<string, double> scores = new() { ["S1"] = 1 };
        var brief = new QueryBrief("résumé", labels, [], [], [], null);
        var plan = new SearchQueryPlan("quoi", [new("quoi", "original")], [], false, brief);
        var report = new RetrievalReport("lexical", 1, 1, 1, labels, Decisions:
            [new("S1", id, "hash", 0, 1, 1, "retained", hits)], Rerank: new("ok", "model", 0, scores),
            Plan: plan, Classification: new("rules", 1, 0, [new("S1", "relevant", "support", 1, 1, scores, true)]));
        var facts = new[] { new TemporalFact("k", "quote", "user", 1, default, "hash", 0, 5, false, "fact") };
        using var schema = JsonDocument.Parse("""{"type":"object"}""");
        var options = new ChatOptions
        {
            ResponseSchema = schema.RootElement, SearchPlan = plan, RetrievalReport = report,
            EligibleSourceIds = scope, PinnedSourceIds = scope, Recall = new(facts, 1, 1, []),
            SourceCandidates = [new(id, "a.md", "hash", "s", "text", 0, 1, 1, 1, "a", references, true)]
        };
        var frozen = options.Copy();
        labels[0] = references[0] = "changed";
        scope.Clear();
        hits[0] = new(3, 8, 0, 0);
        scores.Clear();
        facts[0] = facts[0] with { Quote = "changed" };
        schema.Dispose();
        Assert.Contains(id, frozen.EligibleSourceIds!);
        Assert.Contains(id, frozen.PinnedSourceIds!);
        Assert.Equal("S1", frozen.SourceCandidates![0].References[0]);
        Assert.Equal("initial", frozen.SearchPlan!.Brief!.Labels[0]);
        Assert.Equal("initial", frozen.RetrievalReport!.Plan!.Brief!.Labels[0]);
        Assert.Equal("initial", frozen.RetrievalReport.Omissions[0]);
        Assert.Equal(1, frozen.RetrievalReport.Decisions![0].Queries![0].Rank);
        Assert.Equal(1, frozen.RetrievalReport.Classification!.Candidates[0].Facets["S1"]);
        Assert.Equal(1, frozen.RetrievalReport.Rerank!.Scores!["S1"]);
        Assert.Equal("quote", frozen.Recall!.Facts[0].Quote);
        Assert.Equal("object", frozen.ResponseSchema!.Value.GetProperty("type").GetString());
    }
}
