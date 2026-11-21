using System.Collections.Frozen;
using Alycia.Contracts.Retrieval;
using Alycia.Modules.Testing;

namespace Alycia.Modules.Retrieval.Tests;

public sealed class PipelineTests
{
    [Fact]
    public async Task The_semantic_callback_receives_only_the_authorized_originals()
    {
        var owner = Guid.NewGuid();
        SourceChunk Chunk(string name) => new(Guid.NewGuid(), name, "hash", name, "txt", 0, name.Length, 1, 1, name, [], true);
        var first = Chunk("orion");
        var hidden = Chunk("autre");
        var pipeline = new RetrievalPipeline(new SnapshotIndex(owner, [first, hidden]), new());
        var policy = new PassageRankingSettings(false, false, 4, 1, .6, .3, false, 16, 100, null);
        var request = new RetrievalRequest("hybrid", new[] {first.FileId}.ToFrozenSet(), policy, false, null);
        var calls = 0;
        var result = await pipeline.PrepareAsync(owner, "orion", request, (text, chunks, ct) =>
        {
            Assert.Equal(first.FileId, Assert.Single(chunks).FileId);
            calls++;
            return Task.FromResult(new SemanticSearch([1], 1, []));
        }, default);
        Assert.Equal(1, calls);
        Assert.Equal(first.FileId, Assert.Single(result.Chunks).FileId);
        Assert.Empty((await pipeline.PrepareAsync(Guid.NewGuid(), "orion", request, null, default)).Chunks);
    }
}
