using System.Text;
using System.Text.Json;
using Alycia.Modules.Chat.Application;
using Alycia.Modules.Chat.Infrastructure;

namespace Alycia.Modules.Chat.Tests;

public sealed class TokenBudgetTests
{
    [Fact]
    public async Task ModelMetadataCapsTheRequestedWindowAndAliasChangesInvalidateIt()
    {
        var handler = new Metadata();
        using var http = new HttpClient(handler) { BaseAddress = new("http://127.0.0.1:11434/") };
        var model = ChatFixture.Ollama(http, new());
        var options = new ChatOptions { ContextTokens = 32768, TokenCounting = "conservative" };
        Assert.Equal(4096, (await model.PrepareOptionsAsync(handler.Name, options, default)).ContextTokens);
        Assert.Equal(32768, options.ContextTokens);
        handler.Digest = "changed"; handler.Window = 8192;
        Assert.Equal(8192, (await model.PrepareOptionsAsync(handler.Name, options, default)).ContextTokens);
        Assert.Equal(2048, (await model.PrepareOptionsAsync(handler.Name, new() { ContextTokens = 2048, TokenCounting = "conservative" }, default)).ContextTokens);
    }

    private sealed class Metadata : HttpMessageHandler
    {
        public string Name = "budget-" + Guid.NewGuid().ToString("N");
        public string Digest = "initial"; public int Window = 4096;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            object value = request.RequestUri!.AbsolutePath == "/api/tags"
                ? new { models = new[] { new { name = Name, digest = Digest } } }
                : new
                {
                    capabilities = new[] { "completion", "tools" },
                    model_info = new Dictionary<string, object>
                    { ["general.architecture"] = "test", ["test.context_length"] = Window }
                };
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            { Content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json") });
        }
    }

    [Fact]
    public void MatchesIndependentHuggingFaceCountsForFrenchCodeAndCjk()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "qwen-counter.json")));
        var counter = QwenTokenCounter.FromMetadata(document.RootElement.GetProperty("metadata"))!;
        foreach (var sample in document.RootElement.GetProperty("cases").EnumerateArray())
            Assert.Equal(sample.GetProperty("count").GetInt32(), counter.Count(sample.GetProperty("text").GetString()!));
    }
    [Fact]
    public void InstalledByteBpeVocabularyCountsMergesAndKeepsConservativeFallbacks()
    {
        var extra = 256;
        var chars = Enumerable.Range(0, 256).Select(i => ((char)(i is >= 33 and <= 126 or >= 161 and <= 172 or >= 174 ? i : extra++)).ToString()).ToList();
        var merges = new[] { "h e", "he l", "hel l", "hell o" };
        chars.AddRange(["he", "hel", "hell", "hello"]);
        var info = JsonSerializer.SerializeToElement(new Dictionary<string, object>
        { ["tokenizer.ggml.pre"] = "qwen2", ["tokenizer.ggml.model"] = "gpt2", ["tokenizer.ggml.tokens"] = chars, ["tokenizer.ggml.merges"] = merges });
        var counter = QwenTokenCounter.FromMetadata(info)!;
        Assert.NotNull(counter);
        Assert.Equal(1, counter.Count("hello"));
        Assert.Equal(3, counter.Count("hello hello"));
        Assert.Equal(Encoding.UTF8.GetByteCount("hello 🌍"), counter.Count("hello 🌍"));
        Assert.Equal(Encoding.UTF8.GetByteCount("été"), counter.Count("été"));
        Assert.Null(QwenTokenCounter.FromMetadata(JsonSerializer.SerializeToElement(new { })));
        Assert.Equal(33, TokenBudget.Count("hello", new() { TokenCounter = counter }));
        Assert.True(TokenBudget.Count("hello", new()) >= 5);
    }

    [Fact]
    public async Task CountsSummaryAndToolRoundsWithoutReplacingMissingMetricsWithZero()
    {
        var meter = new MeteredChatModel(new Measured());
        var settings = new ChatOptions { CallPurpose = "summary" };
        await meter.GenerateAsync("demo", [], settings, default);
        await meter.StreamWithToolsAsync("demo", [], new(), _ => Task.CompletedTask, default);
        var usage = meter.Snapshot(new());
        Assert.Equal(["summary", "tools"], usage.Calls.Select(c => c.Purpose));
        Assert.Equal(20, usage.InputTokens);
        Assert.Equal(4, usage.OutputTokens);
        Assert.True(usage.Complete);
        Assert.Null(usage.Calls[0].FirstContentMilliseconds);
        Assert.NotNull(usage.Calls[1].FirstContentMilliseconds);
        Assert.InRange(usage.Calls[1].FirstContentMilliseconds!.Value, 0, usage.Calls[1].ElapsedMilliseconds + 1);
        var unknown = new MeteredChatModel(new Measured(missing: true));
        await unknown.GenerateAsync("demo", [], settings, default);
        Assert.Null(unknown.Snapshot(settings).InputTokens);
        Assert.False(unknown.Snapshot(settings).Complete);
    }

    private sealed class Measured(bool missing = false) : IChatModel
    {
        public Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<string>>([]);
        public Task<string> GenerateAsync(string model, IReadOnlyList<PromptMessage> messages, CancellationToken ct) => Task.FromResult("résumé");
        public Task<GenerationReply> GenerateReplyAsync(string model, IReadOnlyList<PromptMessage> messages, ChatOptions options, CancellationToken ct) =>
            Task.FromResult(new GenerationReply("résumé", missing ? null : new(10, 2, 100, 50, "stop")));
        public async Task<GenerationReply> StreamWithToolsAsync(string model, IReadOnlyList<PromptMessage> messages, ChatOptions options, Func<ModelDelta, Task> delta, CancellationToken ct)
        {
            await delta(new("", Status: "préparation"));
            await delta(new("résumé"));
            return await GenerateReplyAsync(model, messages, options, ct);
        }
    }
}
