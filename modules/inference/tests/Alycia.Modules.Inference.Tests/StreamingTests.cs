using System.Net;
using System.Text.Json;
using System.Text;

namespace Alycia.Modules.Chat.Tests;

public sealed class StreamingTests
{
    [Fact]
    public async Task StreamsThinkingSeparatelyAndReportsFinalStatistics()
    {
        using var http = new HttpClient(new Handler(async (request, ct) =>
        {
            if (request.RequestUri!.AbsolutePath == "/api/tags") return Response("{\"models\":[{\"name\":\"demo\"}]}");
            if (request.RequestUri.AbsolutePath == "/api/show") return Response("{\"capabilities\":[\"completion\"]}");
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
            Assert.True(body.RootElement.GetProperty("stream").GetBoolean());
            Assert.Equal(-1, body.RootElement.GetProperty("options").GetProperty("num_predict").GetInt32());
            Assert.False(body.RootElement.GetProperty("options").TryGetProperty("seed", out _));
            Assert.Equal(0.1, body.RootElement.GetProperty("options").GetProperty("temperature").GetDouble());
            return Response("""
                {"message":{"role":"assistant","thinking":"réflexion","content":""},"done":false}
                {"message":{"role":"assistant","content":"Bonjour "},"done":false}
                {"message":{"role":"assistant","content":"🙂"},"done":true,"eval_count":5,"prompt_eval_count":10,"eval_duration":1000000000,"done_reason":"stop"}
                """);
        }))
        { BaseAddress = new Uri("http://127.0.0.1:11434/") };
        var deltas = new List<ModelDelta>();
        var reply = await InferenceTestProvider.Ollama(http, new()).StreamAsync("demo", [new("user", "hi")],
            new ProviderSettings() { Temperature = 0.1 }.InferenceCall(), delta => { deltas.Add(delta); return Task.CompletedTask; }, default);
        Assert.Equal("Bonjour 🙂", reply.Content);
        Assert.Equal("réflexion", deltas[0].Thinking);
        Assert.Equal(5, reply.Stats!.OutputTokens);
        Assert.Equal("stop", reply.Stats.DoneReason);
    }

    [Theory]
    [InlineData("{\"message\":{\"content\":\"partial\"},\"done\":false}\n")]
    [InlineData("{\"error\":\"private provider detail\"}\n")]
    [InlineData("{\"done\":true,\"message\":{\"content\":\"\"}}\n")]
    [InlineData("not json\n")]
    public async Task RejectsMissingCompletionAndInvalidStreams(string payload)
    {
        using var http = new HttpClient(new Handler((request, _) => Task.FromResult(Response(
            request.RequestUri!.AbsolutePath == "/api/tags" ? "{\"models\":[{\"name\":\"demo\"}]}"
                : request.RequestUri.AbsolutePath == "/api/show" ? "{\"capabilities\":[\"completion\"]}" : payload))))
        { BaseAddress = new Uri("http://127.0.0.1:11434/") };
        await Assert.ThrowsAsync<ChatModelException>(() => InferenceTestProvider.Ollama(http, new())
            .StreamAsync("demo", [], new ProviderSettings().InferenceCall(), _ => Task.CompletedTask, default));
    }

    [Theory]
    [InlineData("length")]
    [InlineData("unknown")]
    [InlineData(null)]
    public async Task ContinuousModeDoesNotTreatATruncatedOrUnknownEndAsSuccess(string? reason)
    {
        var payload = JsonSerializer.Serialize(new { message = new { content = "partial" }, done = true, done_reason = reason });
        using var http = new HttpClient(new Handler((request, _) => Task.FromResult(Response(
            request.RequestUri!.AbsolutePath == "/api/tags" ? "{\"models\":[{\"name\":\"demo\"}]}"
                : request.RequestUri.AbsolutePath == "/api/show" ? "{\"capabilities\":[\"completion\"]}" : payload))))
        { BaseAddress = new Uri("http://127.0.0.1:11434/") };
        await Assert.ThrowsAsync<ChatModelException>(() => InferenceTestProvider.Ollama(http, new())
            .StreamAsync("demo", [], new ProviderSettings().InferenceCall(), _ => Task.CompletedTask, default));
        // Explicit bounded mode remains available, including for summarization.
        var bounded = await InferenceTestProvider.Ollama(http, new()).StreamAsync("demo", [],
            new ProviderSettings() { GenerateUntilStop = false }.InferenceCall(), _ => Task.CompletedTask, default);
        Assert.Equal("partial", bounded.Content);
    }

    [Fact]
    public async Task ActiveStreamOutlivesTheConfiguredInactivityTimeout()
    {
        using var http = new HttpClient(new Handler((request, _) => Task.FromResult(
            request.RequestUri!.AbsolutePath == "/api/tags" ? Response("{\"models\":[{\"name\":\"demo\"}]}")
                : request.RequestUri.AbsolutePath == "/api/show" ? Response("{\"capabilities\":[\"completion\"]}")
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new PacedStream()) })))
        { BaseAddress = new Uri("http://127.0.0.1:11434/") };
        var reply = await InferenceTestProvider.Ollama(http, new()).StreamAsync("demo", [],
            new ProviderSettings() { TimeoutSeconds = 1 }.InferenceCall(), _ => Task.CompletedTask, default);
        Assert.Equal("continued", reply.Content);
    }

    private sealed class PacedStream : MemoryStream
    {
        private int _part;
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            if (_part == 3) return 0;
            await Task.Delay(450, ct);
            var text = _part++ switch
            {
                0 => "{\"message\":{\"content\":\"con\"},\"done\":false}\n",
                1 => "{\"message\":{\"content\":\"tinued\"},\"done\":false}\n",
                _ => "{\"done\":true,\"done_reason\":\"stop\"}\n"
            };
            return Encoding.UTF8.GetBytes(text, buffer.Span);
        }
    }

    private static HttpResponseMessage Response(string text) => new(HttpStatusCode.OK)
    { Content = new StringContent(text, Encoding.UTF8, "application/x-ndjson") };

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> action) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => action(request, ct); }
}
