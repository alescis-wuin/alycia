using Alycia.Modules.Inference;
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
}
