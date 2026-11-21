using Alycia.Contracts.Retrieval;
using Alycia.Contracts.Indexing;
using Alycia.Modules.Chat.Application;
using Alycia.Modules.Chat.Domain;
using Alycia.Modules.Chat.Infrastructure;

namespace Alycia.Modules.Chat.Tests;

public sealed class ProjectRetrievalTests
{
    [Fact]
    public void NoMatchStaysEmptyUnlessExplicitlyPinned()
    {
        var c = Chunk("data.txt", "data", "orion quota");
        var file = new Attachment { Id = c.FileId, Name = c.Path, Text = c.Text, Sha256 = c.Sha256, ContextMode = "project" };
        var ranked = ProjectRetrieval.Rank([c], "badminton Neptune");
        Assert.Empty(ranked);
        Assert.Empty(ProjectRetrieval.Pack(ranked, [file.ToTurnFile()], 1000, new()).Sources);
        file.ContextMode = "pinned";
        Assert.Equal("épinglé explicitement", Assert.Single(ProjectRetrieval.Pack(ranked, [file.ToTurnFile()], 1000, new()).Sources).Reason);
    }

    [Fact]
    public void CompositeContextKeepsOriginalEvidenceBesideBoundedOrientation()
    {
        var c = Chunk("code.cs", "Store", "orion quota 37");
        var file = new Attachment { Id = c.FileId, Name = c.Path, Text = c.Text, Sha256 = c.Sha256, ContextMode = "project" };
        var summary = new Attachment { Id = Guid.NewGuid(), Name = "Synthèse", Text = new string('x', 5000), Sha256 = "derived", ContextMode = "pinned", Status = "derived_summary" };
        var result = ProjectRetrieval.Pack([c], [file.ToTurnFile(), summary.ToTurnFile()], 2000, new());
        Assert.Contains(result.Sources, s => s.Id == file.Id);
        Assert.InRange(result.Sources.Single(s => s.Id == summary.Id).Utf8Bytes, 1, 500);
        Assert.True(TokenBudget.Count(result.Text, new()) <= 2000);
    }

    private static SourceChunk Chunk(string path, string symbol, string text) => new(Guid.NewGuid(), path, "hash", symbol, "method", 0, text.Length, 1, 1, text, [], true);
    [Fact]
    public void ExactSymbolRanksAboveUnrelatedDocumentationAndPackingDeduplicates()
    {
        var implementation = Chunk("src/Store.cs", "Store.ReadAsync()", "return await database.ReadAsync(id);");
        var docs = Chunk("README.md", "Introduction", "Alycia est une application de conversation locale.");
        var copy = implementation with { FileId = Guid.NewGuid(), Path = "copy.cs" };
        var ranked = ProjectRetrieval.Rank([docs, implementation, copy], "Où est implémenté Store.ReadAsync ?");
        Assert.NotEqual(docs.FileId, ranked[0].FileId);
        var files = ranked.Select(c => new Attachment { Id = c.FileId, Name = c.Path, Sha256 = c.Sha256, Text = c.Text, ContextMode = "project" }).ToArray();
        var packed = ProjectRetrieval.Pack(ranked, files.Select(f => f.ToTurnFile()).ToArray(), 1500, new());
        Assert.Single(packed.Sources, c => c.Path.EndsWith(".cs"));
        Assert.All(packed.Sources, s => Assert.NotNull(s.Reference));
        Assert.True(TokenBudget.Count(packed.Text, new()) <= 1500);
    }
    [Fact]
    public void SemanticRankingCanRetrieveAParaphraseAndExclusionsAlwaysWin()
    {
        var answer = Chunk("src/A.cs", "Purge", "remove obsolete records");
        var other = Chunk("src/B.cs", "Color", "render red button");
        var ranked = ProjectRetrieval.Rank([other, answer], "Supprimer les anciennes données", [.01, .99]);
        Assert.Equal(answer, ranked[0]);
        var files = new[] { new Attachment { Id = answer.FileId, Name = answer.Path, Sha256 = answer.Sha256, Text = answer.Text, ContextMode = "excluded" } };
        Assert.Empty(ProjectRetrieval.Pack(ranked, files.Select(f => f.ToTurnFile()).ToArray(), 1000, new()).Sources);
    }
    [Fact]
    public void PartialRangeDoesNotClaimUnreadLines()
    {
        var text = string.Join('\n', Enumerable.Range(0, 100).Select(i => "source line " + i));
        var c = Chunk("src/A.txt", "data", text) with { EndLine = 100 };
        var files = new[] { new Attachment { Id = c.FileId, Name = c.Path, Sha256 = c.Sha256, Text = text, ContextMode = "project" } };
        var result = ProjectRetrieval.Pack([c], files.Select(f => f.ToTurnFile()).ToArray(), 600, new());
        Assert.Single(result.Sources);
        Assert.True(result.Sources[0].Partial);
        Assert.True(result.Sources[0].EndLine < 100);
        Assert.DoesNotContain(":L1-L100", result.Text);
    }
    [Fact]
    public async Task HybridSearchUsesOnlyPreparedVectorsAndReportsPendingCoverage()
    {
        var semantic = new PreparedEmbeddings();
        var service = ChatFixture.Retrieval(new Index(), semantic);
        var options = new ChatOptions { RetrievalMode = "hybrid", EmbeddingModel = "embed" };
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
        var ranked = ProjectRetrieval.Rank([c, copy, b, a], "store", lexicalScores: [10, 9, 9, 10]);
        Assert.Equal(new[] { a, b, c }, ranked);
    }

    [Fact]
    public async Task HybridSearchHasNo2048PassageQuota()
    {
        var service = ChatFixture.Retrieval(new LargeIndex(), new PreparedEmbeddings { Ready = true });
        var result = await service.PrepareAsync(Guid.NewGuid(), "database", new ChatOptions { RetrievalMode = "hybrid", EmbeddingModel = "embed" }.ProjectSearch(), default);
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
