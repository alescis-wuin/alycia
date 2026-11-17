using System.Net;
using System.Text;
using System.Text.Json;
using Alycia.Modules.Chat.Application;
using Alycia.Modules.Chat.Infrastructure;

namespace Alycia.Modules.Chat.Tests;

public sealed class OllamaChatModelTests
{
    [Fact]
    public async Task GenerationsAndEmbeddingsShareTheLocalInferenceLimit()
    {
        var active = 0; var maximum = 0;
        using var http = Client(async (request, ct) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/api/tags") return Json("{\"models\":[{\"name\":\"demo\"}]}");
            if (path == "/api/show") return Json("{\"capabilities\":[\"completion\"]}");
            var current = Interlocked.Increment(ref active); maximum = Math.Max(maximum, current);
            try
            {
                await Task.Delay(15, ct);
                return Json(path == "/api/embed" ? "{\"embeddings\":[[1,0]]}"
                    : "{\"done\":true,\"done_reason\":\"stop\",\"message\":{\"role\":\"assistant\",\"content\":\"ok\"}}");
            }
            finally { Interlocked.Decrement(ref active); }
        });
        var model = new OllamaChatModel(http, new());
        var embeddings = new OllamaEmbeddings(http);
        await Task.WhenAll(model.GenerateAsync("demo", [], default), embeddings.EmbedAsync("embed", ["source"], default), model.GenerateAsync("demo", [], default));
        Assert.Equal(1, maximum);
    }

    [Theory]
    [InlineData("chat", "la génération")]
    [InlineData("summary", "le résumé interne")]
    [InlineData("memory", "l’extraction de mémoire")]
    public async Task EmptyContentIdentifiesStageAndRetainsMetricsWithoutExposingThinking(string purpose, string stage)
    {
        using var http = Client(async (request, ct) =>
        {
            if (request.RequestUri!.AbsolutePath == "/api/tags") return Json("{\"models\":[{\"name\":\"demo\"}]}");
            if (request.RequestUri.AbsolutePath == "/api/show") return Json("{\"capabilities\":[\"completion\",\"thinking\"]}");
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
            if (purpose == "chat") Assert.False(body.RootElement.TryGetProperty("think", out _));
            else Assert.False(body.RootElement.GetProperty("think").GetBoolean());
            return Json("""
                {"done":true,"done_reason":"length","prompt_eval_count":500,"eval_count":128,
                 "message":{"role":"assistant","content":"","thinking":"PRIVATE_THINKING"}}
                """);
        });
        var model = new MeteredChatModel(new OllamaChatModel(http, new()));
        var options = new ChatOptions { CallPurpose = purpose };
        var error = await Assert.ThrowsAsync<ChatModelException>(() => model.GenerateAsync("demo", [], options, default));
        Assert.Equal(ChatModelFailure.EmptyContent, error.Failure);
        Assert.Contains(stage, error.Message);
        Assert.Contains("Limite de génération", error.Message);
        Assert.DoesNotContain("PRIVATE_THINKING", error.Message);
        Assert.Equal(128, model.Snapshot(options).OutputTokens);
    }

    [Fact]
    public async Task SendsHistoryAndExplicitGenerationBudget()
    {
        using var http = Client(async (request, ct) =>
        {
            if (request.RequestUri!.AbsolutePath == "/api/tags")
            {
                return Json("{\"models\":[{\"name\":\"demo\"}]}");
            }
            if (request.RequestUri.AbsolutePath == "/api/show") return Json("{\"capabilities\":[\"completion\"]}");

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
        var model = new OllamaChatModel(http, new ChatOptions { GenerateUntilStop = false });
        Assert.Equal("Bonjour", await model.GenerateAsync("demo", [new("system", "system"), new("user", "hello")], default));
    }

    [Fact]
    public async Task ExcludesCloudAliasesFromLocalModels()
    {
        using var http = Client((request, _) => Task.FromResult(Json(request.RequestUri!.AbsolutePath == "/api/show"
            ? "{\"capabilities\":[\"completion\"]}" : """
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
            request.RequestUri!.AbsolutePath == "/api/tags" ? "{\"models\":[{\"name\":\"demo\"}]}"
                : request.RequestUri.AbsolutePath == "/api/show" ? "{\"capabilities\":[\"completion\"]}" : body)));
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

    [Fact]
    public async Task ListsOnlyLocalCompletionModelsUsingCapabilitiesNotNames()
    {
        var inspected = new System.Collections.Concurrent.ConcurrentBag<string>();
        using var http = Client(async (request, ct) =>
        {
            if (request.RequestUri!.AbsolutePath == "/api/tags") return Json("""
                {"models":[{"name":"vector-alias"},{"name":"chat-z"},{"name":"chat-a"},{"name":"chat-a"},
                {"name":"remote:cloud"},{"name":"remote-alias","remote_host":"https://example.com"},
                {"name":"remote-show"},{"name":"removed"},{"name":"vision-only"}]}
                """);
            Assert.Equal("/api/show", request.RequestUri.AbsolutePath);
            Assert.Equal(HttpMethod.Post, request.Method);
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
            var name = body.RootElement.GetProperty("model").GetString()!;
            Assert.False(body.RootElement.GetProperty("verbose").GetBoolean());
            inspected.Add(name);
            return name switch
            {
                "vector-alias" => Json("{\"capabilities\":[\"embedding\"]}"),
                "vision-only" => Json("{\"capabilities\":[\"vision\"]}"),
                "remote-show" => Json("{\"capabilities\":[\"completion\"],\"remote_model\":\"hosted\"}"),
                "removed" => new(HttpStatusCode.NotFound),
                _ => Json("{\"capabilities\":[\"completion\",\"embedding\"]}")
            };
        });
        Assert.Equal(["chat-a", "chat-z"], await new OllamaChatModel(http, new()).ListModelsAsync(default));
        Assert.Equal(6, inspected.Count);
        Assert.DoesNotContain("remote:cloud", inspected);
        Assert.DoesNotContain("remote-alias", inspected);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExistingEmbeddingConversationIsRejectedBeforeAnyChatRequest(bool streaming)
    {
        using var http = Client((request, _) => Task.FromResult(request.RequestUri!.AbsolutePath switch
        {
            "/api/tags" => Json("{\"models\":[{\"name\":\"embeddinggemma:latest\"}]}"),
            "/api/show" => Json("{\"capabilities\":[\"embedding\"]}"),
            _ => throw new InvalidOperationException("An embedding model must never receive /api/chat.")
        }));
        var model = new OllamaChatModel(http, new());
        var error = await Assert.ThrowsAsync<ChatModelException>(async () =>
        {
            if (streaming) await model.StreamAsync("embeddinggemma:latest", [], new(), _ => Task.CompletedTask, default);
            else await model.GenerateAsync("embeddinggemma:latest", [], default);
        });
        Assert.Contains("embeddinggemma:latest", error.Message);
        Assert.Contains("embeddings", error.Message);
        Assert.Contains("Réglages", error.Message);
    }

    [Fact]
    public async Task RevalidatesCapabilitiesWhenAnAliasChangesAfterListing()
    {
        var shows = 0;
        using var http = Client((request, _) => Task.FromResult(request.RequestUri!.AbsolutePath switch
        {
            "/api/tags" => Json("{\"models\":[{\"name\":\"local\"}]}"),
            "/api/show" => Json(++shows == 1 ? "{\"capabilities\":[\"completion\"]}" : "{\"capabilities\":[\"embedding\"]}"),
            _ => throw new InvalidOperationException("Changed alias must not generate.")
        }));
        var model = new OllamaChatModel(http, new());
        Assert.Equal(["local"], await model.ListModelsAsync(default));
        await Assert.ThrowsAsync<ChatModelException>(() => model.StreamAsync("local", [], new(), _ => Task.CompletedTask, default));
        Assert.Equal(2, shows);
    }

    [Fact]
    public async Task MissingCapabilityMetadataIsNotAssumedToSupportGeneration()
    {
        using var http = Client((request, _) => Task.FromResult(Json(request.RequestUri!.AbsolutePath == "/api/tags"
            ? "{\"models\":[{\"name\":\"local\"}]}" : "{}")));
        var error = await Assert.ThrowsAsync<ChatModelException>(() => new OllamaChatModel(http, new()).ListModelsAsync(default));
        Assert.Contains("capacités", error.Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RejectedGenerationIdentifiesTheActualModel(bool streaming)
    {
        using var http = Client((request, _) => Task.FromResult(request.RequestUri!.AbsolutePath switch
        {
            "/api/tags" => Json("{\"models\":[{\"name\":\"local\"}]}"),
            "/api/show" => Json("{\"capabilities\":[\"completion\"]}"),
            _ => new(HttpStatusCode.BadRequest) { Content = new StringContent("private provider detail") }
        }));
        var model = new OllamaChatModel(http, new());
        var error = await Assert.ThrowsAsync<ChatModelException>(async () =>
        {
            if (streaming) await model.StreamAsync("local", [], new(), _ => Task.CompletedTask, default);
            else await model.GenerateAsync("local", [], default);
        });
        Assert.Contains("local", error.Message);
        Assert.Contains("HTTP 400", error.Message);
        Assert.DoesNotContain("private provider detail", error.Message);
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
