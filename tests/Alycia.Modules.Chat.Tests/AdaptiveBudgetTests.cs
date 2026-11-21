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
        var result = await ProjectAgent.RunAsync(model, tools, Guid.NewGuid(), "local",
            new([new("system", "SYSTEM"), new("user", "QUESTION")], 0, 64), settings, null, default);
        Assert.Equal("Terminé", result.Content);
        Assert.Equal(30, tools.Calls);
        Assert.Equal(30, result.Tools!.Count);
        Assert.True(model.Summaries > 0);
        Assert.All(result.Tools, t => Assert.InRange(t.RetainedTokens, 1, 1800));
        Assert.True(result.Tools.Sum(t => t.ResultBytes) > settings.ToolContextTokens);
        Assert.All(model.Inputs, input => Assert.Contains(input, m => m.Content == "QUESTION"));
    }

    [Fact]
    public void EmbeddingChunksPreserveEveryUnicodeCharacterAndCoordinates()
    {
        var text = string.Concat(Enumerable.Repeat("é🙂漢字\n", 2000));
        var chunk = new SourceChunk(Guid.NewGuid(), "a.txt", "sha", "text", "text", 10, 10 + text.Length, 4, 2004, text, [], true);
        var parts = EmbeddingChunks.Split(chunk, 129).ToArray();
        Assert.Equal(text, string.Concat(parts.Select(p => p.Text)));
        Assert.All(parts, p => { Assert.False(char.IsLowSurrogate(p.Text[0])); Assert.False(char.IsHighSurrogate(p.Text[^1])); });
        Assert.Equal(chunk.Start, parts[0].Start); Assert.Equal(chunk.End, parts[^1].End);
        Assert.Equal(parts.Length, parts.Select(EmbeddingChunks.Key).Distinct().Count());
        Assert.NotEqual(EmbeddingChunks.Key(parts[0]), EmbeddingChunks.Key(parts[0] with { Sha256 = "new" }));
    }

    [Fact]
    public void NewSettingsRoundTripWithoutLosingExplicitValues()
    {
        var options = new ChatOptions
        {
            ToolMaxCalls = 300,
            ToolContextTokens = 4096,
            ToolResultBytes = 131072,
            ToolTimeoutSeconds = 3600,
            DocumentContextTokens = 2048,
            EmbeddingBatchSize = 2,
            EmbeddingChunkCharacters = 512,
            EmbedHistory = false,
            IndexMaxCharacters = 0
        };
        var json = ConversationConfiguration.Serialize(options);
        var copy = JsonSerializer.Deserialize<ChatOptions>(json, ProjectToolCatalog.Json)!;
        Assert.Equal(json, ConversationConfiguration.Serialize(copy));
        Assert.Equal(0, new ChatOptions().ToolMaxCalls);
        Assert.Equal(0, new ChatOptions().IndexMaxCharacters);
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
        public Task<GenerationReply> StreamWithToolsAsync(string name, IReadOnlyList<PromptMessage> messages, ChatOptions settings, Func<ModelDelta, Task> delta, CancellationToken ct)
        {
            Inputs.Add(messages);
            return Task.FromResult(Inputs.Count > 30 ? new GenerationReply("Terminé") : new GenerationReply("", ToolCalls:
                [new(new("project_read", JsonSerializer.SerializeToElement(new { fileId = Guid.NewGuid() })))]));
        }
    }
}
