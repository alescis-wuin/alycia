using System.Text.Json;
using Alycia.Modules.Chat.Application;

namespace Alycia.Modules.Chat.Tests;

public sealed class AdaptiveBudgetTests
{
    [Fact]
    public async Task DiscardedResultsFreeBudgetAndMoreThan24CallsCanComplete()
    {
        var model = new ReadingModel(); var tools = new ReadingTools();
        var settings = new ChatOptions { ContextTokens = 32768, ToolContextTokens = 1800, ToolResultBytes = 2048 };
        var result = await ProjectAgent.RunAsync(RecursiveSummarizer.Create, model, tools, Guid.NewGuid(), "local",
            new ContextWindow([new("system", "SYSTEM"), new("user", "QUESTION")], 0, 64).ToolPrompt(), settings.ToolGeneration(), null, default);
        Assert.Equal("Terminé", result.Content);
        Assert.Equal(30, tools.Calls);
        Assert.Equal(30, result.Tools!.Count);
        Assert.True(model.Summaries > 0);
        Assert.All(result.Tools, t => Assert.InRange(t.RetainedTokens, 1, 1800));
        Assert.True(result.Tools.Sum(t => t.ResultBytes) > settings.ToolContextTokens);
        Assert.All(model.Inputs, input => Assert.Contains(input, m => m.Content == "QUESTION"));
    }

    private sealed class ReadingTools : IProjectTools
    {
        public int Calls;
        public Task<bool> HasSourcesAsync(Guid id, CancellationToken ct) => Task.FromResult(true);
        public Task<ProjectToolResult> CallAsync(Guid id, string name, JsonElement args, int budget, CancellationToken ct)
        { Calls++; return Task.FromResult(new ProjectToolResult(new string('x', Math.Min(600, budget)))); }
    }
    private sealed class ReadingModel : IChatModel
    {
        public int Summaries;
        public List<IReadOnlyList<PromptMessage>> Inputs { get; } = [];
        public Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<string>>(["local"]);
        public Task<string> GenerateAsync(string name, IReadOnlyList<PromptMessage> messages, CancellationToken ct)
        { Summaries++; return Task.FromResult("Résumé des sources précédentes."); }
        public Task<ModelReply> StreamWithToolsAsync(string name, IReadOnlyList<PromptMessage> messages, InferenceCall settings, Func<ModelDelta, Task> delta, CancellationToken ct)
        {
            Inputs.Add(messages);
            return Task.FromResult(Inputs.Count > 30 ? new ModelReply("Terminé") : new ModelReply("", ToolCalls:
                [new(new("project_read", JsonSerializer.SerializeToElement(new { fileId = Guid.NewGuid() })))]));
        }
    }
}
