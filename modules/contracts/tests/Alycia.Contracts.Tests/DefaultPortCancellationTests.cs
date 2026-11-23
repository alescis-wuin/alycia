using Alycia.Contracts.Indexing;
using Alycia.Contracts.Inference;
using Alycia.Contracts.Retrieval;

namespace Alycia.Modules.Chat.Tests;

public sealed class DefaultPortCancellationTests
{
    private sealed class Provider(Action? generating = null) : IChatModel
    {
        public Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<string>>(["fixture"]);
        public Task<string> GenerateAsync(string model, IReadOnlyList<PromptMessage> messages, CancellationToken ct)
        {
            generating?.Invoke();
            return Task.FromResult("réponse");
        }
    }

    private sealed class Embeddings : IIndexedEmbeddings
    {
        public Task<SemanticSearch> SearchAsync(Guid conversation, string query, EmbeddingQuery settings,
            IReadOnlyList<SourceChunk> chunks, CancellationToken ct) => throw new InvalidOperationException();
    }

    [Theory]
    [InlineData("inspection")]
    [InlineData("vram")]
    [InlineData("tools")]
    [InlineData("stream-tools")]
    [InlineData("generate")]
    [InlineData("search-memory")]
    [InlineData("search-history")]
    public async Task Optional_ports_propagate_the_original_cancellation(string operation)
    {
        IChatModel provider = new Provider();
        IIndexedEmbeddings embeddings = new Embeddings();
        var ct = new CancellationToken(true);
        var settings = EmbeddingQuery.Capture(new ChatSettings());
        Task Call() => operation switch
        {
            "inspection" => provider.InspectAsync("fixture", "auto", ct),
            "vram" => provider.ResidentVramAsync("fixture", ct),
            "tools" => provider.SupportsToolsAsync("fixture", ct),
            "stream-tools" => provider.StreamWithToolsAsync("fixture", [], InferenceCall.Default, _ => Task.CompletedTask, ct),
            "generate" => provider.GenerateReplyAsync("fixture", [], InferenceCall.Default, ct),
            "search-memory" => embeddings.SearchMemoryAsync(Guid.Empty, "x", settings, 1, DateTimeOffset.UnixEpoch, ct),
            "search-history" => embeddings.SearchHistoryAsync(Guid.Empty, "x", settings, [], ct),
            _ => throw new ArgumentOutOfRangeException(nameof(operation))
        };
        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(Call);
        Assert.Equal(ct, error.CancellationToken);
    }

    [Fact]
    public async Task Cancelled_fallback_generation_does_not_emit_a_delta()
    {
        using var cancel = new CancellationTokenSource();
        IChatModel provider = new Provider(cancel.Cancel);
        var deltas = new List<ModelDelta>();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => provider.StreamAsync("fixture", [], InferenceCall.Default,
            delta => { deltas.Add(delta); return Task.CompletedTask; }, cancel.Token));
        Assert.Empty(deltas);
    }
}
