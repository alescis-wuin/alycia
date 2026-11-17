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
            Assert.Single((await service.PrepareAsync(id, "store", options, default)).Calls);
            encoder.Identity = "embed:different";
            Assert.Equal(2, (await service.PrepareAsync(id, "store", options, default)).Calls.Count);
            Assert.Equal(5, encoder.Calls);
            Assert.Throws<ChatModelException>(() => ProjectRetrievalService.Cosine([1, 0], [1]));
        }
        finally { Directory.Delete(root, true); }
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
