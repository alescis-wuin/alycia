using System.Net;
using System.Text;
using System.Text.Json;
using Alycia.Modules.Chat.Application;
using Alycia.Modules.Chat.Infrastructure;

namespace Alycia.Modules.Chat.Tests;

public sealed class OllamaChatModelTests
{
    [Fact]
    public async Task SendsHistoryAndExplicitGenerationBudget()
    {
        using var http = Client(async (request, ct) =>
        {
            if (request.RequestUri!.AbsolutePath == "/api/tags")
            {
                return Json("{\"models\":[{\"name\":\"demo\"}]}");
            }

            Assert.Equal("/api/chat", request.RequestUri.AbsolutePath);
            using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
            var root = json.RootElement;
            Assert.Equal("demo", root.GetProperty("model").GetString());
            Assert.False(root.GetProperty("stream").GetBoolean());
            Assert.Equal(8192, root.GetProperty("options").GetProperty("num_ctx").GetInt32());
            Assert.Equal(1024, root.GetProperty("options").GetProperty("num_predict").GetInt32());
            Assert.Equal("system", root.GetProperty("messages")[0].GetProperty("role").GetString());
            return Json("{\"done\":true,\"message\":{\"role\":\"assistant\",\"content\":\"Bonjour\"}}");
        });
        var model = new OllamaChatModel(http, new ChatOptions());
        Assert.Equal("Bonjour", await model.GenerateAsync("demo", [new("system", "system"), new("user", "hello")], default));
    }

    [Fact]
    public async Task ExcludesCloudAliasesFromLocalModels()
    {
        using var http = Client((_, _) => Task.FromResult(Json("""
            {"models":[{"name":"local"},{"name":"remote:cloud"},{"name":"alias","remote_model":"remote"},
            {"name":"alias2","remote_host":"https://example.com"}]}
            """)));
        Assert.Equal(["local"], await new OllamaChatModel(http, new()).ListModelsAsync(default));
    }

    [Theory]
    [InlineData("{\"done\":false,\"message\":{\"role\":\"assistant\",\"content\":\"partial\"}}")]
    [InlineData("{\"done\":true,\"message\":{\"role\":\"assistant\",\"content\":\"\"}}")]
    [InlineData("not json")]
    public async Task RejectsMalformedOrIncompleteReply(string body)
    {
        using var http = Client((request, _) => Task.FromResult(Json(
            request.RequestUri!.AbsolutePath == "/api/tags" ? "{\"models\":[{\"name\":\"demo\"}]}" : body)));
        await Assert.ThrowsAsync<ChatModelException>(() => new OllamaChatModel(http, new())
            .GenerateAsync("demo", [new("user", "hello")], default));
    }

    [Fact]
    public async Task CancellationReachesOllama()
    {
        using var http = Client(async (_, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return Json("{}");
        });
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new OllamaChatModel(http, new())
            .GenerateAsync("demo", [new("user", "hello")], cancel.Token));
    }

    private static HttpResponseMessage Json(string text) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(text, Encoding.UTF8, "application/json")
    };

    private static HttpClient Client(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> action) =>
        new(new Handler(action)) { BaseAddress = new Uri("http://127.0.0.1:11434/") };

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> action) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => action(request, ct);
    }
}
