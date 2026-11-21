using Alycia.Contracts.Indexing;
using Alycia.Modules.Chat.Application;
using Alycia.Modules.Chat.Infrastructure;

namespace Alycia.Modules.Chat.Tests;

public sealed class EmbeddingIndexBoundaryTests
{
    [Fact]
    public async Task CancellationDuringAReadOnlyVectorLookupReachesTheCaller()
    {
        using var cancel = new CancellationTokenSource();
        var encoder = new QueryEncoder();
        var store = new CancellingReader(cancel);
        var search = new IndexedEmbeddingSearch(store, encoder);
        var chunk = new SourceChunk(Guid.NewGuid(), "corpus.txt", "hash", "", "text",
            0, 14, 1, 1, "private corpus", [], true);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => search.SearchAsync(Guid.NewGuid(), "question",
            EmbeddingQuery.Capture(new ChatOptions { EmbeddingModel = "fixture" }), [chunk], cancel.Token));

        Assert.Equal(["question"], encoder.Inputs);
        Assert.Equal([EmbeddingChunks.Key(chunk)], store.Keys);
    }

    [Theory]
    [InlineData("running", 0, true)]
    [InlineData("partial", 0, true)]
    [InlineData("paused", 31, false)]
    [InlineData("failed", 31, false)]
    [InlineData("completed", 29, false)]
    [InlineData("completed", 30, true)]
    public void PersistedJobDeterminesWhenItCanResume(string state, int elapsedSeconds, bool due)
    {
        var updated = DateTimeOffset.UnixEpoch;
        var job = new EmbeddingJob(new(Guid.NewGuid(), false, state, 0, 0, "", "fixture", updated),
            new("fixture", 16, 1600, true), 1, "");
        Assert.Equal(due, job.IsDue(updated.AddSeconds(elapsedSeconds)));
    }

    private sealed class QueryEncoder : ILocalEmbeddings
    {
        public List<string> Inputs { get; } = [];

        public Task<string> IdentityAsync(string model, CancellationToken ct) => Task.FromResult("fixture:digest");

        public Task<EmbeddingBatch> EmbedAsync(string model, IReadOnlyList<string> input, CancellationToken ct)
        {
            Inputs.AddRange(input);
            return Task.FromResult(new EmbeddingBatch([[1, 0]], null));
        }
    }

    private sealed class CancellingReader(CancellationTokenSource cancel) : IEmbeddingSimilarityReader
    {
        public string[] Keys { get; private set; } = [];

        public async Task<Dictionary<string, double>> SimilaritiesAsync(string identity, string[] keys, float[] query, CancellationToken ct)
        {
            Keys = keys;
            await cancel.CancelAsync();
            ct.ThrowIfCancellationRequested();
            return [];
        }

        public Task<Dictionary<int, double>> MemorySimilaritiesAsync(Guid owner, string identity, int through, DateTimeOffset before,
            float[] query, CancellationToken ct) => throw new NotSupportedException();
    }
}
