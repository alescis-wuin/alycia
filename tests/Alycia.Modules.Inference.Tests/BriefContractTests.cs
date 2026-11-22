using System.Text.Json;
using Alycia.Contracts.Chat;
using Alycia.Contracts.Retrieval;
using Alycia.Modules.Testing;

namespace Alycia.Modules.Inference.Tests;

public sealed class BriefContractTests
{
    [Fact]
    public async Task Refiners_admit_only_verbatim_intents_and_do_not_alias_caller_arrays()
    {
        const string question = "Où acheter le module Alpha en France ?";
        foreach (var candidate in new[] { "le module Alpha", "le module Beta" })
        {
            var model = new ScriptedTextGeneration([JsonSerializer.Serialize(new { intent = candidate })], identity: "recorded-model");
            foreach (var provider in new IQueryBriefRefiner[] { new QueryBriefRefiner(model), new RecordedQueryBrief(_ => candidate, "recorded-model") })
            {
                var original = new QueryBrief("initial", [], ["Alpha"], [], [], null, question);
                var config = new BriefGeneration(ScriptedTextGeneration.Model, 4096, 128, 15);
                var result = await provider.RefineAsync(original, config, default);
                Assert.Equal(candidate.Contains("Beta") ? "initial" : "le module Alpha Alpha", result.Summary);
                Assert.Equal(candidate.Contains("Beta") ? "rules-v1" : "recorded-model", result.Method);
                original.Literals[0] = "muté";
                Assert.Equal("Alpha", Assert.Single(result.Literals));
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => provider.RefineAsync(original, config, new(true)));
            }
        }
    }
}
