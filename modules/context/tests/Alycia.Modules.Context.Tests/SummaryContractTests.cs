using Alycia.Contracts.Chat;
using Alycia.Contracts.Context;
using Alycia.Modules.Testing;

namespace Alycia.Modules.Context.Tests;

public sealed class SummaryContractTests
{
    [Fact]
    public async Task Summary_providers_respect_empty_content_byte_budgets_call_limits_and_cancellation()
    {
        var model = new ScriptedTextGeneration(["résumé"]);
        var settings = new SummaryParameters(4096, 256, 1, 3, null, 10, "conservative", null, null);
        foreach (var reducer in new ISummaryReducer[] { new RecursiveSummarizer(model, ScriptedTextGeneration.Model, settings),
            new RecordedSummaries(_ => "résumé", 1) })
        {
            var sources = new[] { new SummarySource("origine", new string('a', 300)) };
            Assert.Contains("Aucun", await reducer.ReduceAsync([], 128, default));
            Assert.Equal(0, reducer.Calls);
            await Assert.ThrowsAsync<ArgumentException>(() => reducer.ReduceAsync(sources, 100, default));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => reducer.ReduceAsync([], 128, new(true)));
            Assert.Equal("résumé", await reducer.ReduceAsync(sources, 128, default));
            Assert.Equal(1, reducer.Calls);
            await Assert.ThrowsAsync<ChatModelException>(() => reducer.ReduceAsync(sources, 128, default));
            Assert.Equal(1, reducer.Calls);
        }
    }
}
