using System.Net;
using System.Text.Json;
using System.Text;

namespace Alycia.Modules.Chat.Tests;

public sealed class ToolCallingTests
{
    [Fact]
    public async Task NativeToolOnlyTurnPreservesThinkingAndStructuredArguments()
    {
        using var http = Http("""
            {"message":{"role":"assistant","thinking":"Lire le code","content":""},"done":false}
            {"message":{"role":"assistant","content":"","tool_calls":[{"function":{"name":"project_list","arguments":{"codeOnly":true}}}]},"done":true,"done_reason":"stop"}
            """);
        var model = InferenceTestProvider.Ollama(http, new());
        Assert.True(await model.SupportsToolsAsync("demo", default));
        var reply = await model.StreamWithToolsAsync("demo", [new("user", "Analyse")], new ProviderSettings().InferenceCall(), _ => Task.CompletedTask, default);
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
        await Assert.ThrowsAsync<ChatModelException>(() => InferenceTestProvider.Ollama(http, new()).StreamWithToolsAsync("demo", [], new ProviderSettings().InferenceCall(), _ => Task.CompletedTask, default));
    }

    [Fact]
    public async Task UnsupportedModelCannotReceiveToolPayload()
    {
        using var http = Http("unused", supports: false);
        await Assert.ThrowsAsync<ChatModelException>(() => InferenceTestProvider.Ollama(http, new()).StreamWithToolsAsync("demo", [], new ProviderSettings().InferenceCall(), _ => Task.CompletedTask, default));
    }

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
}
