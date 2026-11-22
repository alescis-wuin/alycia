using Alycia.Modules.Inference;
using System.Net;
using System.Text;
using System.Text.Json;
using Alycia.Modules.Chat.Application;
using Alycia.Modules.Chat.Infrastructure;

namespace Alycia.Modules.Chat.Tests;

public sealed class ToolSummaryRecoveryTests
{
    [Theory]
    [InlineData("stop")]
    [InlineData("length")]
    public async Task NativeEmptySummaryContinuesUnderBudgetAndAllowsRereadingCompactedSources(string reason)
    {
        var handler = new Provider(reason);
        using var http = Client(handler);
        var model = new MeteredChatModel(ChatFixture.Ollama(http, new()));
        var sources = new Sources();
        var original = Context();
        var events = new List<ModelDelta>();
        var options = new ChatOptions();
        var result = await ProjectAgent.RunAsync(RecursiveSummarizer.Create, model, sources, Guid.NewGuid(), "demo", original.ToolPrompt(), options.ToolGeneration(),
            delta => { events.Add(delta); return Task.CompletedTask; }, default);

        Assert.Equal("Réponse finale", result.Content);
        Assert.Equal(["A", "B", "C", "A"], sources.Reads);
        Assert.All(result.Tools!, tool => Assert.True(tool.Succeeded && !tool.RepeatSuppressed));
        Assert.Equal(ToolMemoryFallback.Warning, Assert.Single(result.Warnings!));
        Assert.Single(events, e => e.Status == ToolMemoryFallback.Warning);
        Assert.Equal(1, handler.Summaries);
        Assert.Contains(handler.Inputs[^1], m => m.Content.Contains("REPLI EXTRACTIF"));
        Assert.Contains(handler.Inputs[^1], m => m.Role == "tool" && m.Content == sources.LastResult);
        Assert.All(handler.Inputs, input =>
        {
            Assert.StartsWith("SYSTEM", input[0].Content);
            Assert.Equal(original.Messages.Skip(1), input.Skip(1).Take(5));
        });
        Assert.Equal("SYSTEM", original.Messages[0].Content);
        var usage = model.Snapshot(options.ModelIdentity, options.TokenCounter);
        Assert.True(usage.Complete);
        var summary = Assert.Single(usage.Calls, c => c.Purpose == "summary");
        Assert.Equal(256, summary.Stats!.OutputTokens);
        Assert.Equal(reason, summary.Stats.DoneReason);
        Assert.Equal(256 + 5 * 12, usage.OutputTokens);
        Assert.All(usage.Calls.Where(c => c.Purpose == "tools"), call =>
            Assert.InRange(call.EstimatedInputTokens, 1, options.ContextTokens - options.MaxOutputTokens - options.SafetyTokens));
    }

    [Theory]
    [InlineData("incomplete")]
    [InlineData("network")]
    [InlineData("cancel")]
    public async Task TransportFailuresAndCancellationNeverBecomeAnExtractiveSuccess(string failure)
    {
        using var cancel = new CancellationTokenSource();
        var handler = new Provider(failure, cancel);
        using var http = Client(handler);
        var sources = new Sources();
        var events = new List<ModelDelta>();
        Task Run() => ProjectAgent.RunAsync(RecursiveSummarizer.Create, ChatFixture.Ollama(http, new()), sources, Guid.NewGuid(), "demo", Context().ToolPrompt(), new ChatOptions().ToolGeneration(),
            delta => { events.Add(delta); return Task.CompletedTask; }, cancel.Token);
        if (failure == "cancel") await Assert.ThrowsAnyAsync<OperationCanceledException>(Run);
        else
        {
            var error = await Assert.ThrowsAsync<ChatModelException>(Run);
            Assert.NotEqual(ChatModelFailure.EmptyContent, error.Failure);
            if (failure == "incomplete") Assert.Contains("réponse incomplète pendant le résumé interne", error.Message);
        }
        Assert.Equal(1, handler.Summaries);
        Assert.InRange(sources.Reads.Count, 1, 3);
        Assert.DoesNotContain(events, e => e.Status == ToolMemoryFallback.Warning);
    }

    [Fact]
    public void ExtractiveFallbackIsBoundedUnicodeAndExplicitAboutOmissions()
    {
        var text = string.Concat(Enumerable.Repeat("é🙂漢字", 100));
        var extract = ToolMemoryFallback.Extract([new("Mémoire", text), new("src/A.cs", text)], 512);
        Assert.InRange(Encoding.UTF8.GetByteCount(extract), 1, 512);
        Assert.StartsWith("REPLI EXTRACTIF : portions omises", extract);
        Assert.Contains("[src/A.cs]", extract);
        Assert.Contains("é🙂漢字", extract);
        Assert.DoesNotContain("\uFFFD", extract);
    }

    private static ContextWindow Context() => new([new("system", "SYSTEM"), new("user", "u1"), new("assistant", "a1"),
        new("user", "u2"), new("assistant", "a2"), new("user", "QUESTION")], 4, 128);
    private static HttpClient Client(HttpMessageHandler handler) => new(handler) { BaseAddress = new("http://127.0.0.1:11434/") };

    private sealed class Sources : IProjectTools
    {
        public List<string> Reads { get; } = [];
        public string LastResult { get; private set; } = "";
        public Task<bool> HasSourcesAsync(Guid id, CancellationToken ct) => Task.FromResult(true);
        public Task<ProjectToolResult> CallAsync(Guid id, string name, JsonElement arguments, int budget, CancellationToken ct)
        {
            var file = arguments.GetProperty("fileId").GetString()!;
            Reads.Add(file);
            LastResult = file + new string('x', Math.Min(5000, budget) - 1);
            return Task.FromResult(new ProjectToolResult(LastResult));
        }
    }

    private sealed class Provider(string reason, CancellationTokenSource? cancel = null) : HttpMessageHandler
    {
        public List<PromptMessage[]> Inputs { get; } = [];
        public int Summaries;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.RequestUri!.AbsolutePath == "/api/tags") return Json("{\"models\":[{\"name\":\"demo\"}]}");
            if (request.RequestUri.AbsolutePath == "/api/show") return Json("{\"capabilities\":[\"completion\",\"tools\",\"thinking\"]}");
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
            var root = body.RootElement;
            if (root.GetProperty("stream").GetBoolean())
            {
                Assert.False(root.TryGetProperty("think", out _));
                Inputs.Add(root.GetProperty("messages").Deserialize<PromptMessage[]>(ProjectToolCatalog.Json)!);
                var reads = new[] { "A", "B", "C", "A" };
                var calls = Inputs.Count > reads.Length ? Array.Empty<ModelToolCall>()
                    : [new ModelToolCall(new("project_read", JsonSerializer.SerializeToElement(new { fileId = reads[Inputs.Count - 1] })))];
                return Json(JsonSerializer.Serialize(new
                {
                    message = new { role = "assistant", content = calls.Length == 0 ? "Réponse finale" : "", tool_calls = calls },
                    done = true,
                    done_reason = "stop",
                    prompt_eval_count = 100,
                    eval_count = 12
                }, ProjectToolCatalog.Json));
            }
            Summaries++;
            Assert.False(root.GetProperty("think").GetBoolean());
            Assert.InRange(root.GetProperty("options").GetProperty("num_predict").GetInt32(), 256, 1024);
            if (reason == "network") throw new HttpRequestException("Provider disconnected");
            if (reason == "cancel") cancel!.Cancel();
            return Json(JsonSerializer.Serialize(new
            {
                message = new { role = "assistant", content = "", thinking = "PRIVATE_THINKING" },
                done = reason != "incomplete",
                done_reason = reason,
                prompt_eval_count = 4000,
                eval_count = 256
            }));
        }
        private static HttpResponseMessage Json(string text) => new(HttpStatusCode.OK)
        { Content = new StringContent(text + "\n", Encoding.UTF8, "application/json") };
    }
}
