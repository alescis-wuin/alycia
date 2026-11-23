
namespace Alycia.Modules.Chat.Tests;

public sealed class ProjectRetrievalTests
{
    private static SourceChunk Chunk(string path, string symbol, string text) => new(Guid.NewGuid(), path, "hash", symbol, "method", 0, text.Length, 1, 1, text, [], true);

    [Fact]
    public async Task HybridSearchUsesOnlyPreparedVectorsAndReportsPendingCoverage()
    {
        var semantic = new PreparedEmbeddings();
        var service = RetrievalTestInputs.Service(new Index(), semantic);
        var options = new ChatSettings { RetrievalMode = "hybrid", EmbeddingModel = "embed" };
        var first = await service.PrepareAsync(Guid.NewGuid(), "database", options.ProjectSearch(), default);
        Assert.Equal(1, semantic.Calls);
        Assert.Equal(1, first.Report.Cache!.VectorMisses);
        Assert.Contains(first.Report.Omissions, s => s.Contains("0/1"));
        Assert.Single(first.Chunks); // BM25 still works while the background job is pending.
        semantic.Ready = true;
        var second = await service.PrepareAsync(Guid.NewGuid(), "paraphrase", options.ProjectSearch(), default);
        Assert.Single(second.Chunks);
        Assert.Equal(1, second.Report.Cache!.VectorHits);
        Assert.Empty(second.Report.Omissions);
        Assert.False(second.Report.Cache.VectorWritten); // Chat does not write corpus vectors.
    }

    [Fact]
    public void LexicalCacheInvalidatesChangedContentAndSeparatesConversations()
    {
        var cache = new LexicalIndexCache(); var id = Guid.NewGuid();
        var chunk = Chunk("src/A.cs", "Read", "Orion quota 37");
        var first = cache.Get(id, [chunk]);
        Assert.False(first.Hit);
        Assert.True(first.Index.Score("Orion")[0] > 0);
        Assert.True(cache.Get(id, [chunk]).Hit);
        Assert.False(cache.Get(Guid.NewGuid(), [chunk]).Hit);
        var changed = cache.Get(id, [chunk with { Text = "Vega quota 49" }]);
        Assert.False(changed.Hit);
        Assert.Equal(0, changed.Index.Score("Orion")[0]);
        Assert.True(changed.Index.Score("Vega")[0] > 0);
        Assert.False(cache.Get(id, [chunk with { Sha256 = "different" }]).Hit);
        for (var i = 0; i < 4; i++) cache.Get(Guid.NewGuid(), [chunk]);
        Assert.False(cache.Get(id, [chunk]).Hit);
    }

    [Fact]
    public void DiversityOrderingIsStableWithDuplicatesAndFileTies()
    {
        var a = Chunk("A.cs", "Store", "store alpha");
        var b = Chunk("B.cs", "Store", "store beta");
        var c = a with { Start = 20, End = 31, Text = "store gamma" };
        var copy = b with { FileId = Guid.NewGuid(), Path = "Z.cs" };
        var ranked = PassageSearch.Rank([c, copy, b, a], "store", lexicalScores: [10, 9, 9, 10]);
        Assert.Equal(new[] { a, b, c }, ranked);
    }

    [Fact]
    public async Task HybridSearchHasNo2048PassageQuota()
    {
        var service = RetrievalTestInputs.Service(new LargeIndex(), new PreparedEmbeddings { Ready = true });
        var result = await service.PrepareAsync(Guid.NewGuid(), "database", new ChatSettings { RetrievalMode = "hybrid", EmbeddingModel = "embed" }.ProjectSearch(), default);
        Assert.Equal(3000, result.Report.CandidateChunks);
        Assert.Equal(3000, result.Report.Cache!.VectorHits);
    }

    private sealed class LargeIndex : IProjectIndex
    {
        public Task<ProjectIndexSnapshot> BuildAsync(Guid id, CancellationToken ct) => Task.FromResult(new ProjectIndexSnapshot(
            Enumerable.Range(0, 3000).Select(i => Chunk("src/" + i + ".cs", "Read", "database " + i)).ToArray(), 3000, 3000, 0, []));
    }

    private sealed class PreparedEmbeddings : IIndexedEmbeddings
    {
        public int Calls; public bool Ready;
        public Task<SemanticSearch> SearchAsync(Guid id, string query, EmbeddingQuery settings, IReadOnlyList<SourceChunk> chunks, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(new SemanticSearch(chunks.Select(_ => Ready ? .9 : 0).ToArray(), Ready ? chunks.Count : 0, []));
        }
    }

    private sealed class Index : IProjectIndex
    {
        private readonly SourceChunk _chunk = Chunk("src/A.cs", "Read", "database.Read();");
        public Task<ProjectIndexSnapshot> BuildAsync(Guid conversation, CancellationToken ct) => Task.FromResult(new ProjectIndexSnapshot([_chunk], 1, 1, 0, []));
    }
}
