using Alycia.Modules.Retrieval;
using Alycia.Contracts.Retrieval;
using Alycia.Contracts.Indexing;
using Alycia.Modules.Chat.Application;
using Alycia.Modules.Chat.Domain;
using Alycia.Modules.Chat.Infrastructure;

namespace Alycia.Modules.Chat.Tests;

public class RoutingAndRerankTests
{
    [Theory]
    [InlineData("Bonjour !", "direct")]
    [InlineData("Quelle décision avions-nous prise ?", "memory")]
    [InlineData("Find the method in Plan.cs", "retrieval")]
    [InlineData("Compare les deux versions", "explore")]
    public void RoutingUsesIntentAndRespectsToolPermission(string question, string mode)
    {
        var settings = new ChatOptions { RoutingMode = "auto", UseProjectTools = false };
        var route = QueryPlanner.Decide(question, settings.RoutingMode, settings.UseProjectTools, [new Attachment { Name = "Plan.cs", ContextMode = "project" }.ToTurnFile()]);
        Assert.Equal(mode, route.Mode);
        Assert.False(route.Tools);
        Assert.True(QueryPlanner.Decide("Bonjour", settings.RoutingMode, settings.UseProjectTools, [new Attachment { Name = "memo.md", ContextMode = "pinned" }.ToTurnFile()]).Search);
    }

    [Fact]
    public void UnavailableNamedReadsDoNotExposeExcludedFiles()
    {
        var options = new ChatOptions { RoutingMode = "auto", UseProjectTools = true };
        var question = "Lis secret.md";
        var other = new Attachment { Name = "public.md", ContextMode = "project" };
        var hidden = new Attachment { Name = "secret.md", ContextMode = "excluded" };
        var absent = QueryPlanner.Decide(question, options.RoutingMode, options.UseProjectTools, [other.ToTurnFile()]);
        Assert.Equal("unavailable", absent.Mode);
        Assert.False(absent.Search);
        Assert.False(absent.Tools);
        Assert.Equal(absent, QueryPlanner.Decide(question, options.RoutingMode, options.UseProjectTools, [other.ToTurnFile(), hidden.ToTurnFile()]));
        Assert.True(QueryPlanner.Decide("Lis public.md", options.RoutingMode, options.UseProjectTools, [other.ToTurnFile(), hidden.ToTurnFile()]).Search);
    }

    [Fact]
    public async Task TiesTimeoutAndCallerCancellationPreserveTheRankContract()
    {
        var corpus = new[] { "nord.cs", "est.cs", "sud.cs" }.Select(path => new SourceChunk(Guid.NewGuid(),
            path, "sha", "Orion", "text", 0, path.Length, 1, 1, path, [], true)).ToArray();
        var id = Guid.NewGuid();
        var baseline = (await ChatFixture.Retrieval(new FixtureIndex(corpus)).PrepareAsync(id, "Orion", new ChatOptions().ProjectSearch(), default)).Chunks;
        var options = new ChatOptions { RerankEnabled = true, RerankCandidateLimit = 2, RerankTimeoutMs = 50 };
        var stable = await ChatFixture.Retrieval(new FixtureIndex(corpus), reranker: new FakeReranker(false))
            .PrepareAsync(id, "Orion", options.ProjectSearch(), default);
        Assert.Equal(baseline.Select(SourceCoordinates.Reference), stable.Chunks.Select(SourceCoordinates.Reference));
        Assert.Equal(2, stable.Report.Rerank!.Scores!.Count);
        var delayed = ChatFixture.Retrieval(new FixtureIndex(corpus), reranker: new FakeReranker(true));
        var fallback = await delayed.PrepareAsync(id, "Orion", options.ProjectSearch(), default);
        Assert.Equal("timeout", fallback.Report.Rerank!.State);
        Assert.Equal(baseline.Select(SourceCoordinates.Reference), fallback.Chunks.Select(SourceCoordinates.Reference));
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => delayed.PrepareAsync(id, "Orion", options.ProjectSearch(), cancelled.Token));
    }
    [Fact]
    public async Task FacetEmbeddingsRerankerAndDiagnosticsOnlySeeAdmittedSources()
    {
        var visible = new SourceChunk(Guid.Parse("00000000-0000-0000-0000-000000000001"), "public.cs",
            "public-hash", "Orion", "code", 0, 10, 1, 1, "Orion path", [], true);
        var excluded = visible with
        {
            FileId = Guid.Parse("00000000-0000-0000-0000-000000000002"),
            Path = "private.cs",
            Text = "Orion private"
        };
        var provider = new RecordingRanker();
        var service = ChatFixture.Retrieval(new FixtureIndex([excluded, visible]), provider, reranker: provider);
        var options = new ChatOptions
        {
            RetrievalMode = "hybrid",
            FacetPlanningEnabled = true,
            RerankEnabled = true,
            EligibleSourceIds = new HashSet<Guid> { visible.FileId },
            PinnedSourceIds = new HashSet<Guid> { excluded.FileId },
            SearchPlan = new("Orion", [new("Orion", "lexical"), new("alternative", "semantic")], [], false)
        };
        var result = await service.PrepareAsync(Guid.NewGuid(), "Orion", options.ProjectSearch(), default);
        Assert.Equal(new[] { "Orion", "alternative" }, provider.Searches.Select(item => item.Query));
        Assert.All(provider.Searches, search => Assert.Equal(visible.FileId, Assert.Single(search.Ids)));
        Assert.Equal(visible.FileId, Assert.Single(provider.Reranked));
        Assert.Equal(visible, Assert.Single(result.Chunks));
        var decision = Assert.Single(result.Report.Decisions!);
        Assert.Equal(SourceCoordinates.Reference(visible), decision.Reference);
        Assert.Equal(new[] { 0, 1 }, decision.Queries!.Select(hit => hit.Query));
    }

    private sealed class RecordingRanker : IIndexedEmbeddings, IPassageReranker
    {
        public List<(string Query, Guid[] Ids)> Searches { get; } = [];
        public Guid[] Reranked { get; private set; } = [];
        public Task<SemanticSearch> SearchAsync(Guid id, string query, EmbeddingQuery settings, IReadOnlyList<SourceChunk> chunks, CancellationToken ct)
        {
            Searches.Add((query, chunks.Select(chunk => chunk.FileId).ToArray()));
            return Task.FromResult(new SemanticSearch(Enumerable.Repeat(1.0, chunks.Count).ToArray(), chunks.Count, []));
        }

        public Task<RerankScores> ScoreAsync(string question, IReadOnlyList<SourceChunk> passages, CancellationToken ct)
        {
            Reranked = passages.Select(chunk => chunk.FileId).ToArray();
            return Task.FromResult(new RerankScores("fixture", new double[passages.Count]));
        }
    }

    private sealed class FixtureIndex(SourceChunk[] chunks) : IProjectIndex
    {
        public Task<ProjectIndexSnapshot> BuildAsync(Guid owner, CancellationToken ct) =>
            Task.FromResult(new ProjectIndexSnapshot(chunks, chunks.Length, chunks.Length, 0, []));
    }
    private sealed class FakeReranker(bool wait) : IPassageReranker
    {
        public async Task<RerankScores> ScoreAsync(string question, IReadOnlyList<SourceChunk> passages, CancellationToken ct)
        {
            if (wait) await Task.Delay(Timeout.Infinite, ct);
            return new("fixture", new double[passages.Count]);
        }
    }

    [Fact]
    public async Task NativeCpuSessionTruncatesPairsWithLocalTokenizer()
    {
        // This graph returns the sum of the attention mask. It has no learned weights.
        const string graph = "CAg6qgEKKQoOYXR0ZW50aW9uX21hc2sSBmZsb2F0cyIEQ2FzdCoJCgJ0bxgBoAECCiwKBmZsb2F0cxIGbG9naXRzIglSZWR1Y2VTdW0qDwoIa2VlcGRpbXMYAKABAhIVc3ludGhldGljLXRva2VuLWNvdW50WiYKDmF0dGVudGlvbl9tYXNrEhQKEggHEg4KAggBCggSBmxlbmd0aGIQCgZsb2dpdHMSBgoECAESAEIECgAQDQ==";
        var dir = Directory.CreateTempSubdirectory();
        try
        {
            dir.CreateSubdirectory("onnx");
            await File.WriteAllBytesAsync(Path.Combine(dir.FullName, "onnx", "model_quantized.onnx"), Convert.FromBase64String(graph));
            await File.WriteAllTextAsync(Path.Combine(dir.FullName, "tokenizer.json"), """
                {"version":"1.0","added_tokens":[],"model":{"type":"WordLevel","vocab":{"[UNK]":0,"x":1},"unk_token":"[UNK]"},"pre_tokenizer":{"type":"Whitespace"}}
                """);
            using var ranker = new OnnxPassageReranker(dir.FullName);
            var chunk = new SourceChunk(Guid.NewGuid(), "x", "hash", "x", "text", 0, 1, 1, 1, "x", [], true);
            var scores = await ranker.ScoreAsync("x", [chunk, chunk with { Text = string.Join(' ', Enumerable.Repeat("x", 980)) }], default);
            Assert.Equal(new double[] { 4, 384 }, scores.Values);
            Assert.StartsWith("onnx-cpu-v1:", scores.Identity);
            File.Delete(Path.Combine(dir.FullName, "tokenizer.json"));
            Assert.Equal(scores.Identity, (await ranker.ScoreAsync("x", [chunk], default)).Identity);
        }
        finally { dir.Delete(true); }
    }
}
