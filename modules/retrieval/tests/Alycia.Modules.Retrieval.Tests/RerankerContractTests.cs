using Alycia.Modules.Retrieval.Onnx;
using Alycia.Modules.Testing;

namespace Alycia.Modules.Chat.Tests;

public sealed class RerankerContractTests
{
    private sealed class WeightsFact : FactAttribute
    {
        public WeightsFact()
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ALYCIA_RERANK_DIRECTORY")))
                Skip = "Poids et tokenizer du reclasseur Horizon requis.";
        }
    }

    [WeightsFact]
    public async Task Native_worker_reads_only_a_snapshot_of_the_submitted_passages()
    {
        using var ranker = new OnnxPassageReranker(Environment.GetEnvironmentVariable("ALYCIA_RERANK_DIRECTORY"));
        var passage = new SourceChunk(Guid.NewGuid(), "Paris.txt", "hash", "Paris", "text", 0, 5, 1, 1, "Paris", [], true);
        var sources = new CallerOwnedList(passage);
        var result = await ranker.ScoreAsync("Paris", sources, default);
        Assert.Single(result.Values);
        Assert.True(double.IsFinite(result.Values[0]));
    }

    private sealed class CallerOwnedList(SourceChunk source) : IReadOnlyList<SourceChunk>
    {
        private readonly int owner = Environment.CurrentManagedThreadId;
        public int Count => 1;
        public SourceChunk this[int index] => Environment.CurrentManagedThreadId == owner && index == 0
            ? source : throw new InvalidOperationException("La collection appartient au thread appelant.");
        public IEnumerator<SourceChunk> GetEnumerator() { yield return this[0]; }
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    [WeightsFact]
    public async Task Verified_weights_and_the_recorded_adapter_preserve_positions_identity_and_cancellation()
    {
        using var neural = new OnnxPassageReranker(Environment.GetEnvironmentVariable("ALYCIA_RERANK_DIRECTORY"));
        SourceChunk Passage(string text) => new(Guid.NewGuid(), "source.txt", "hash", "", "text", 0, text.Length, 1, 1, text, [], true);
        var source = Passage("Paris is the capital of France.");
        var unrelated = Passage("Olive oil is a common ingredient in tomato soup.");
        foreach (var adapter in new IPassageReranker[] {neural, new RecordedReranker("fixture", (q, c) => c.FileId == source.FileId ? 1 : -1)})
        {
            const string question = "What is the capital of France?";
            var first = await adapter.ScoreAsync(question, [source, unrelated], default);
            var reversed = await adapter.ScoreAsync(question, [unrelated, source], default);
            Assert.False(string.IsNullOrWhiteSpace(first.Identity));
            Assert.Equal(first.Identity, reversed.Identity);
            Assert.Equal(2, first.Values.Length);
            Assert.All(first.Values, value => Assert.True(double.IsFinite(value)));
            Assert.True(first.Values[0] > first.Values[1]);
            Assert.Equal(first.Values[0], reversed.Values[1], 5);
            Assert.Equal(first.Values[1], reversed.Values[0], 5);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => adapter.ScoreAsync(question, [source], new(true)));
        }
    }
}
