using Alycia.Modules.Inference;
using Alycia.Contracts.Retrieval;
using Alycia.Modules.Retrieval;
using System.Text.Json;
using Alycia.Modules.Chat.Application;
using Alycia.Modules.Chat.Domain;
using Alycia.Modules.Chat.Infrastructure;

namespace Alycia.Modules.Chat.Tests;

public class ContextQualityTests
{
    [Fact]
    public void ParentIsSearchableButIsNeverAddedToTheOriginalQuote()
    {
        const string text = "# Atlas\n## Archives\n### Conservation\n37 jours\n";
        var parts = CodeStructure.Split(Guid.NewGuid(), "index.md", "sha", text, hierarchical: true);
        var leaf = parts.Single(c => c.Symbol.Contains("Conservation"));
        Assert.Contains("Atlas / Archives / Conservation", leaf.IndexContext);
        Assert.Equal(text[leaf.Start..leaf.End], leaf.Text);
        Assert.DoesNotContain("Atlas", leaf.Text);
        Assert.Contains("Archives", EmbeddingChunks.Text(leaf));
        Assert.NotEqual(EmbeddingChunks.Key(leaf), EmbeddingChunks.Key(leaf with { IndexContext = null }));
        var options = new ChatOptions { HierarchicalIndexEnabled = true, EmbeddingQueryPrefix = "query: " };
        var space = EmbeddingSettings.From(options);
        Assert.NotEqual(space.VectorSpace("m:digest", 256), space.VectorSpace("m:digest", 768));
        Assert.NotEqual(space.VectorSpace("m:digest", 256), (space with { DocumentPrefix = "passage: " }).VectorSpace("m:digest", 256));
    }

    [Fact]
    public async Task PlanStopsAtLimitAndSeparatesUnsearchedFromEmpty()
    {
        var options = new ChatOptions
        {
            AgentPlanningEnabled = true,
            AgentPlanMaxCalls = 1,
            ContextTokens = 32768,
            SearchPlan = QueryPlanning.Build("Compare A.cs et B.cs", [], [], 3)
        };
        var model = new FinalModel(); var tools = new EmptyTools();
        var result = await ProjectAgent.RunAsync(RecursiveSummarizer.Create, model, tools, Guid.NewGuid(), "m", new ContextWindow([new("user", "Compare A.cs et B.cs")], 0, 40).ToolPrompt(), options.ToolGeneration(), null, default);
        Assert.Equal("Réponse limitée", result.Content);
        Assert.Equal("call_limit", result.Research!.StopReason);
        Assert.Equal(new[] { "not_found", "not_searched" }, result.Research.Goals.Select(g => g.State));
        Assert.Single(result.Tools!);
    }

    private sealed class FinalModel : IChatModel
    {
        public Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken ct) => throw new NotSupportedException();
        public Task<string> GenerateAsync(string model, IReadOnlyList<PromptMessage> messages, CancellationToken ct) => Task.FromResult("Réponse limitée");
    }
    private sealed class EmptyTools : IProjectTools
    {
        public Task<bool> HasSourcesAsync(Guid id, CancellationToken ct) => Task.FromResult(true);
        public Task<ProjectToolResult> CallAsync(Guid id, string name, JsonElement args, int budget, CancellationToken ct) =>
            Task.FromResult(new ProjectToolResult("""{"results":[],"nextOffset":null}"""));
    }
}
