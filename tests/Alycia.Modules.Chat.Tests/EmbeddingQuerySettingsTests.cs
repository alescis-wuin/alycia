using Alycia.Modules.Chat.Application;
using Alycia.Modules.Chat.Infrastructure;

namespace Alycia.Modules.Chat.Tests;

public sealed class EmbeddingQuerySettingsTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LookupKeepsItsModelBatchAndVectorSpaceWhenSettingsChange(bool memory)
    {
        var fixture = new VectorLookup();
        var search = new IndexedEmbeddingSearch(fixture, fixture);
        var settings = new ChatOptions
        {
            EmbeddingModel = "first", EmbeddingBatchSize = 2, EmbeddingChunkCharacters = 128,
            EmbeddingQueryPrefix = "q:", Enrichment = new(true)
        };
        var expectedSpace = EmbeddingSettings.From(settings).VectorSpace("first:digest", 2);
        var first = new string('a', 128);
        var second = new string('b', 128);
        var question = first + second + "cc";
        var owner = Guid.NewGuid();
        var source = new SourceChunk(Guid.NewGuid(), "corpus.txt", "hash", "", "text", 0, 4, 1, 1, "text", [], true);
        Task lookup = memory
            ? search.SearchMemoryAsync(owner, question, settings, 2, DateTimeOffset.UnixEpoch, default)
            : search.SearchAsync(owner, question, settings, [source], default);

        Assert.False(lookup.IsCompleted);
        settings.EmbeddingModel = "second";
        settings.EmbeddingBatchSize = 1;
        settings.EmbeddingChunkCharacters = 256;
        settings.EmbeddingQueryPrefix = "changed:";
        fixture.ReleaseIdentity.SetResult();
        await lookup;

        Assert.All(fixture.Models, model => Assert.Equal("first", model));
        Assert.Collection(fixture.Batches,
            batch => Assert.Equal(["q:" + first, "q:" + second], batch),
            batch => Assert.Equal(["q:cc"], batch));
        Assert.Equal(Enumerable.Repeat(expectedSpace, 3), fixture.VectorSpaces);
    }

    private sealed class VectorLookup : ILocalEmbeddings, IEmbeddingSimilarityReader
    {
        public TaskCompletionSource ReleaseIdentity { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public List<string> Models { get; } = [];
        public List<string[]> Batches { get; } = [];
        public List<string> VectorSpaces { get; } = [];

        public async Task<string> IdentityAsync(string model, CancellationToken ct)
        {
            Models.Add(model);
            await ReleaseIdentity.Task.WaitAsync(ct);
            return model + ":digest";
        }

        public Task<EmbeddingBatch> EmbedAsync(string model, IReadOnlyList<string> input, CancellationToken ct)
        {
            Models.Add(model);
            Batches.Add(input.ToArray());
            return Task.FromResult(new EmbeddingBatch(input.Select(_ => new float[] { 1, 0 }).ToArray(), null));
        }

        public Task<Dictionary<string, double>> SimilaritiesAsync(string identity, string[] keys, float[] query, CancellationToken ct)
        {
            VectorSpaces.Add(identity);
            return Task.FromResult(new Dictionary<string, double>());
        }

        public Task<Dictionary<int, double>> MemorySimilaritiesAsync(Guid owner, string identity, int through,
            DateTimeOffset before, float[] query, CancellationToken ct)
        {
            VectorSpaces.Add(identity);
            return Task.FromResult(new Dictionary<int, double>());
        }
    }
}
