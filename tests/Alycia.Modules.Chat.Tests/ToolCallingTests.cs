using System.Net;
using System.Text;
using System.Text.Json;
using Alycia.Modules.Chat.Application;
using Alycia.Modules.Chat.Infrastructure;

namespace Alycia.Modules.Chat.Tests;

public sealed class ToolCallingTests
{
    [Fact]
    public void DefaultWindowReservesAllNineToolsAndStillFitsTheProtectedQuestion()
    {
        var options = TurnContextBudget.WithToolReserve(new ChatOptions().Context());
        var context = SlidingContext.Build(new Alycia.Modules.Context.SlidingHistory(), "Réponds brièvement en français.", [], "Cherche le symbole ReadAsync puis lis son implémentation.", options);
        Assert.True(context.EstimatedInputTokens + options.MaxOutputTokens + options.SafetyTokens <= options.ContextTokens);
    }

    [Fact]
    public async Task NativeToolOnlyTurnPreservesThinkingAndStructuredArguments()
    {
        using var http = Http("""
            {"message":{"role":"assistant","thinking":"Lire le code","content":""},"done":false}
            {"message":{"role":"assistant","content":"","tool_calls":[{"function":{"name":"project_list","arguments":{"codeOnly":true}}}]},"done":true,"done_reason":"stop"}
            """);
        var model = ChatFixture.Ollama(http, new());
        Assert.True(await model.SupportsToolsAsync("demo", default));
        var reply = await model.StreamWithToolsAsync("demo", [new("user", "Analyse")], new ChatOptions().InferenceCall(), _ => Task.CompletedTask, default);
        Assert.Equal("", reply.Content);
        Assert.Equal("Lire le code", reply.Thinking);
        Assert.True(reply.ToolCalls!.Single().Function.Arguments.GetProperty("codeOnly").GetBoolean());
    }

    [Theory]
    [InlineData("{\"message\":{\"tool_calls\":[{\"function\":{\"name\":\"project_read\",\"arguments\":\"invalid\"}}]},\"done\":true,\"done_reason\":\"stop\"}")]
    [InlineData("{\"message\":{\"tool_calls\":[{\"function\":{\"name\":\"project_list\",\"arguments\":{}}}]},\"done\":false}")]
    public async Task IncompleteOrMalformedCallsNeverBecomeExecutable(string response)
    {
        using var http = Http(response);
        await Assert.ThrowsAsync<ChatModelException>(() => ChatFixture.Ollama(http, new()).StreamWithToolsAsync("demo", [], new ChatOptions().InferenceCall(), _ => Task.CompletedTask, default));
    }

    [Fact]
    public async Task UnsupportedModelCannotReceiveToolPayload()
    {
        using var http = Http("unused", supports: false);
        await Assert.ThrowsAsync<ChatModelException>(() => ChatFixture.Ollama(http, new()).StreamWithToolsAsync("demo", [], new ChatOptions().InferenceCall(), _ => Task.CompletedTask, default));
    }

    [Fact]
    public async Task AgentKeepsSmallToolRoundsWithoutSummaryAndPreservesConversation()
    {
        var model = new AgentModel();
        var tools = new Sources();
        var original = Context();
        var events = new List<ModelDelta>();
        var reply = await ProjectAgent.RunAsync(RecursiveSummarizer.Create, model, tools, Guid.NewGuid(), "demo", original.ToolPrompt(), new ChatOptions().ToolGeneration(),
            e => { events.Add(e); return Task.CompletedTask; }, default);
        Assert.Equal("Réponse sourcée", reply.Content);
        Assert.Equal(3, tools.Calls);
        Assert.Equal(3, reply.Tools!.Count);
        Assert.Equal(3, events.Count(e => e.Tool is not null));
        Assert.Equal(0, model.Summaries);
        Assert.Equal(3, model.Inputs[^1].Count(m => m.Role == "tool"));
        Assert.All(model.Inputs, input =>
        {
            Assert.StartsWith("SYSTEM", input[0].Content);
            Assert.Equal(original.Messages.Skip(1).Select(m => m.Content), input.Skip(1).Take(5).Select(m => m.Content));
        });
        Assert.Equal("SYSTEM", original.Messages[0].Content);
        Assert.Contains(model.Inputs[^1], m => m.Role == "tool" && m.ToolName == "project_read");
    }

    [Fact]
    public async Task AgentCompactsOnlyUnderPressureAndKeepsTheLastCompleteRound()
    {
        var model = new AgentModel(); var tools = new Sources(5000);
        var result = await ProjectAgent.RunAsync(RecursiveSummarizer.Create, model, tools, Guid.NewGuid(), "demo", Context().ToolPrompt(), new ChatOptions().ToolGeneration(), null, default);
        Assert.Equal(3, tools.Calls); Assert.True(model.Summaries > 0);
        Assert.Contains(model.Inputs[^1], m => m.Role == "tool" && m.ToolName == "project_read");
        Assert.All(model.Inputs, input => Assert.Contains(input, m => m.Content == "QUESTION"));
        Assert.Equal("Réponse sourcée", result.Content);
    }

    [Fact]
    public async Task ReorderedIdenticalArgumentsDoNotRepeatIoAndStopTheLoop()
    {
        var model = new RepeatingModel(); var tools = new Sources(); var events = new List<ModelDelta>();
        var error = await Assert.ThrowsAsync<ChatModelException>(() => ProjectAgent.RunAsync(RecursiveSummarizer.Create, model, tools, Guid.NewGuid(), "demo", Context().ToolPrompt(), new ChatOptions().ToolGeneration(),
            e => { events.Add(e); return Task.CompletedTask; }, default));
        Assert.Contains("répète", error.Message); Assert.Equal(1, tools.Calls);
        Assert.Equal(2, events.Count(e => e.Tool?.RepeatSuppressed == true));
    }

    [Fact]
    public async Task ToolLimitAndCancellationStopFurtherExecution()
    {
        var limited = new Sources();
        await Assert.ThrowsAsync<ChatModelException>(() => ProjectAgent.RunAsync(RecursiveSummarizer.Create, new AgentModel(), limited, Guid.NewGuid(), "demo",
            Context().ToolPrompt(), new ChatOptions { ToolMaxCalls = 2 }.ToolGeneration(), null, default));
        Assert.Equal(2, limited.Calls);
        using var cancel = new CancellationTokenSource();
        var interrupted = new Sources();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ProjectAgent.RunAsync(RecursiveSummarizer.Create, new AgentModel(), interrupted, Guid.NewGuid(), "demo",
            Context().ToolPrompt(), new ChatOptions().ToolGeneration(), e => { if (e.Tool is not null) cancel.Cancel(); return Task.CompletedTask; }, cancel.Token));
        Assert.Equal(1, interrupted.Calls);
    }

    [Fact]
    public async Task RunningAgentKeepsTheCallLimitCapturedBeforeTheFirstTool()
    {
        var settings = new ChatOptions { ToolMaxCalls = 2 };
        var sources = new Sources();
        var error = await Assert.ThrowsAsync<ChatModelException>(() => ProjectAgent.RunAsync(RecursiveSummarizer.Create,
            new AgentModel(), sources, Guid.NewGuid(), "demo", Context().ToolPrompt(), settings.ToolGeneration(),
            delta =>
            {
                if (delta.Tool is not null) settings.ToolMaxCalls = 0;
                return Task.CompletedTask;
            }, default));
        Assert.Contains("Limite de 2 appels", error.Message);
        Assert.Equal(2, sources.Calls);
    }

    private static ContextWindow Context() => new([new("system", "SYSTEM"), new("user", "u1"), new("assistant", "a1"),
        new("user", "u2"), new("assistant", "a2"), new("user", "QUESTION")], 4, 128);
    private static HttpClient Http(string reply, bool supports = true) => new(new Handler(reply, supports)) { BaseAddress = new("http://127.0.0.1:11434") };
    private sealed class Handler(string reply, bool supports) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            string text;
            if (request.RequestUri!.AbsolutePath == "/api/tags") text = "{\"models\":[{\"name\":\"demo\"}]}";
            else if (request.RequestUri.AbsolutePath == "/api/show") text = supports ? "{\"capabilities\":[\"completion\",\"tools\"]}" : "{\"capabilities\":[\"completion\"]}";
            else
            {
                Assert.True(supports);
                using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
                Assert.Equal(ProjectToolCatalog.All.Select(t => t.Name).Order(), body.RootElement.GetProperty("tools")
                    .EnumerateArray().Select(t => t.GetProperty("function").GetProperty("name").GetString()).Order());
                text = reply;
            }
            return new(HttpStatusCode.OK) { Content = new StringContent(text, Encoding.UTF8, "application/json") };
        }
    }
    private sealed class Sources(int resultSize = 0) : IProjectTools
    {
        public int Calls;
        public Task<bool> HasSourcesAsync(Guid id, CancellationToken ct) => Task.FromResult(true);
        public Task<ProjectToolResult> CallAsync(Guid id, string name, JsonElement args, int budget, CancellationToken ct)
        { Calls++; return Task.FromResult(new ProjectToolResult(resultSize > 0 ? new string('x', Math.Min(resultSize, budget)) : "{\"text\":\"return 42;\",\"nextOffset\":null}", Evidence: new("src/A.cs", new string('a', 64), 0, null, true))); }
    }
    private sealed class RepeatingModel : IChatModel
    {
        private int _calls;
        public Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<string>>(["demo"]);
        public Task<string> GenerateAsync(string model, IReadOnlyList<PromptMessage> messages, CancellationToken ct) => Task.FromResult("résumé");
        public Task<ModelReply> StreamWithToolsAsync(string model, IReadOnlyList<PromptMessage> messages, InferenceCall settings, Func<ModelDelta, Task> delta, CancellationToken ct)
        {
            var args = ++_calls % 2 == 0 ? "{\"offset\":0,\"fileId\":\"file\"}" : "{\"fileId\":\"file\",\"offset\":0}";
            return Task.FromResult(new ModelReply("", ToolCalls: [new(new("project_read", JsonSerializer.Deserialize<JsonElement>(args)))]));
        }
    }
    private sealed class AgentModel : IChatModel
    {
        public List<IReadOnlyList<PromptMessage>> Inputs = [];
        public int Summaries;
        public Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<string>>(["demo"]);
        public Task<string> GenerateAsync(string model, IReadOnlyList<PromptMessage> messages, CancellationToken ct)
        { Summaries++; return Task.FromResult("Source src/A.cs : return 42."); }
        public Task<ModelReply> StreamWithToolsAsync(string model, IReadOnlyList<PromptMessage> messages, InferenceCall settings, Func<ModelDelta, Task> delta, CancellationToken ct)
        {
            Inputs.Add(messages);
            if (Inputs.Count > 3) return Task.FromResult(new ModelReply("Réponse sourcée"));
            return Task.FromResult(new ModelReply("", ToolCalls: [new(new("project_read", JsonSerializer.SerializeToElement(new { fileId = Guid.NewGuid() })))], Thinking: "Consulter"));
        }
    }
}
