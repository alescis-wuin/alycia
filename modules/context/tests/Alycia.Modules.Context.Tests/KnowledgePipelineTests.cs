using Alycia.Contracts.Chat;
using Alycia.Contracts.Context;
using Alycia.Modules.Context;

namespace Alycia.Modules.Chat.Tests;

public sealed class KnowledgePipelineTests
{
    [Fact]
    public async Task RecursiveAnalysisCheckpointsInsideOneLargeFile()
    {
        var model = new SummaryModel();
        var options = new ChatSettings { ContextTokens = 2048, MaxOutputTokens = 256, SafetyTokens = 256, SummaryMaxCalls = 8 };
        var cache = new Dictionary<string, string>();
        RecursiveSummarizer Reducer() => new(model, "fixture", (options.Context().Summary with { ModelIdentity = "fixture" }), null, new(options.SummaryMaxCalls),
            (key, _) => Task.FromResult(cache.GetValueOrDefault(key)), (key, value, _) => { cache.Add(key, value); return Task.CompletedTask; });
        var source = new SummarySource("grand.md", string.Join('\n', Enumerable.Range(0, 430).Select(n => $"Ligne {n:D3} avec des valeurs distinctes pour les contrôles.")));
        await Assert.ThrowsAsync<ChatModelException>(() => Reducer().ReduceAsync([source], 512, default));
        Assert.Equal(8, cache.Count);
        string? final = null;
        for (var cycle = 0; cycle < 12 && final is null; cycle++)
        {
            var count = cache.Count;
            try { final = await Reducer().ReduceAsync([source], 512, default); }
            catch (ChatModelException error) { Assert.Contains("Budget d’appels", error.Message); }
            Assert.InRange(cache.Count - count, 1, options.SummaryMaxCalls);
        }
        Assert.NotNull(final);
        Assert.Equal(model.Inputs.Count, model.Inputs.Distinct().Count());
    }

    private sealed class SummaryModel : IChatModel
    {
        public List<string> Inputs { get; } = [];
        public Task<ModelTokenization> InspectAsync(string model, string tokenCounting, CancellationToken ct) =>
            Task.FromResult(new ModelTokenization("fixture"));
        public Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<string>>(["fixture"]);
        public Task<string> GenerateAsync(string model, IReadOnlyList<PromptMessage> messages, CancellationToken ct)
        {
            Inputs.Add(messages[^1].Content);
            return Task.FromResult("Résumé " + MemoryTransfer.Hash(messages[^1].Content)[..16]);
        }
    }
}
