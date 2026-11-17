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
        Assert.Empty(ProjectRetrieval.Pack(ranked, [file], 1000, new()).Sources);
        file.ContextMode = "pinned";
        Assert.Equal("épinglé explicitement", Assert.Single(ProjectRetrieval.Pack(ranked, [file], 1000, new()).Sources).Reason);
    }

    [Fact]
    public void CompositeContextKeepsOriginalEvidenceBesideBoundedOrientation()
    {
        var c = Chunk("code.cs", "Store", "orion quota 37");
        var file = new Attachment { Id = c.FileId, Name = c.Path, Text = c.Text, Sha256 = c.Sha256, ContextMode = "project" };
        var summary = new Attachment { Id = Guid.NewGuid(), Name = "Synthèse", Text = new string('x', 5000), Sha256 = "derived", ContextMode = "pinned", Status = "derived_summary" };
        var result = ProjectRetrieval.Pack([c], [file, summary], 2000, new());
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
        var packed = ProjectRetrieval.Pack(ranked, files, 1500, new());
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
        Assert.Empty(ProjectRetrieval.Pack(ranked, files, 1000, new()).Sources);
    }
    [Fact]
    public void PartialRangeDoesNotClaimUnreadLines()
    {
        var text = string.Join('\n', Enumerable.Range(0, 100).Select(i => "source line " + i));
        var c = Chunk("src/A.txt", "data", text) with { EndLine = 100 };
        var files = new[] { new Attachment { Id = c.FileId, Name = c.Path, Sha256 = c.Sha256, Text = text, ContextMode = "project" } };
        var result = ProjectRetrieval.Pack([c], files, 600, new());
        Assert.Single(result.Sources);
        Assert.True(result.Sources[0].Partial);
        Assert.True(result.Sources[0].EndLine < 100);
        Assert.DoesNotContain(":L1-L100", result.Text);
    }
    [Fact]
    public async Task EmbeddingsCacheReusesSourcesAndInvalidatesOnModelIdentity()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var id = Guid.NewGuid(); var storage = new FileStorage(root); Directory.CreateDirectory(storage.DirectoryFor(id));
        try
        {
            var encoder = new Embeddings();
            var service = new ProjectRetrievalService(new Index(), encoder, storage);
            var options = new ChatOptions { RetrievalMode = "hybrid", EmbeddingModel = "embed" };
            var first = await service.PrepareAsync(id, "database", options, default);
            Assert.Equal(2, first.Calls.Count);
            Assert.True(first.Report.Cache!.VectorWritten);
            var path = Path.Combine(storage.DirectoryFor(id), "vectors-v1.json");
            var stamp = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            File.SetLastWriteTimeUtc(path, stamp);
            var second = await service.PrepareAsync(id, "store", options, default);
            Assert.Single(second.Calls);
            Assert.True(second.Report.Cache!.LexicalHit);
            Assert.Equal(1, second.Report.Cache.VectorHits);
            Assert.False(second.Report.Cache.VectorWritten);
            Assert.Equal(stamp, File.GetLastWriteTimeUtc(path));
            encoder.Identity = "embed:different";
            Assert.Equal(2, (await service.PrepareAsync(id, "store", options, default)).Calls.Count);
            Assert.Equal(5, encoder.Calls);
            var cache = System.Text.Json.Nodes.JsonNode.Parse(await File.ReadAllTextAsync(path))!;
            var vectors = cache["vectors"]!.AsObject();
            vectors[vectors.First().Key] = new System.Text.Json.Nodes.JsonArray(1);
            await File.WriteAllTextAsync(path, cache.ToJsonString());
            var repaired = await service.PrepareAsync(id, "store", options, default);
            Assert.Equal(2, repaired.Calls.Count);
            Assert.True(repaired.Report.Cache!.VectorWritten);
            Assert.Equal(0, repaired.Report.Cache.VectorHits);
            await File.WriteAllTextAsync(path, "{invalid");
            Assert.Equal(2, (await service.PrepareAsync(id, "store", options, default)).Calls.Count);
            Assert.Throws<ChatModelException>(() => ProjectRetrievalService.Cosine([1, 0], [1]));
        }
        finally { Directory.Delete(root, true); }
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
    public async Task EmbeddingBatchesAreSerializedAcrossConversations()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var encoder = new ConcurrentEmbeddings(); var service = new ProjectRetrievalService(new Index(), encoder, new FileStorage(root));
        try
        {
            var options = new ChatOptions { RetrievalMode = "hybrid", EmbeddingModel = "embed" };
            var results = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => service.PrepareAsync(Guid.NewGuid(), "database", options, default)));
            Assert.Equal(1, encoder.Maximum);
            Assert.All(results, r => Assert.True(r.Report.Cache!.VectorWritten));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
    private sealed class ConcurrentEmbeddings : ILocalEmbeddings
    {
        private int _active; public int Maximum;
        public Task<string> IdentityAsync(string model, CancellationToken ct) => Task.FromResult("test:digest");
        public async Task<EmbeddingBatch> EmbedAsync(string model, IReadOnlyList<string> input, CancellationToken ct)
        {
            var active = Interlocked.Increment(ref _active); Maximum = Math.Max(Maximum, active);
            try { await Task.Delay(10, ct); return new(input.Select(_ => new float[] { 1, 0 }).ToArray(), null); }
            finally { Interlocked.Decrement(ref _active); }
        }
    }

    private sealed class Index : IProjectIndex
    {
        private readonly SourceChunk _chunk = Chunk("src/A.cs", "Read", "database.Read();");
        public Task<ProjectIndexSnapshot> BuildAsync(Guid conversation, CancellationToken ct) => Task.FromResult(new ProjectIndexSnapshot([_chunk], 1, 1, 0, []));
    }
    private sealed class Embeddings : ILocalEmbeddings
    {
        public int Calls; public string Identity = "embed:initial";
        public Task<string> IdentityAsync(string model, CancellationToken ct) => Task.FromResult(Identity);
        public Task<EmbeddingBatch> EmbedAsync(string model, IReadOnlyList<string> input, CancellationToken ct)
        { Calls++; return Task.FromResult(new EmbeddingBatch(input.Select(_ => new float[] { 1, 0 }).ToArray(), new(10, 0, 100, null, "embedding"))); }
    }
}
