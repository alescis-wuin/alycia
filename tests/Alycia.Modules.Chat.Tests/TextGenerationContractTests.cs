using System.Net;
using System.Text;
using System.Text.Json;
using Alycia.Modules.Inference;
using Alycia.Modules.Testing;

namespace Alycia.Modules.Chat.Tests;

public sealed class TextGenerationContractTests
{
    private static readonly GenerationParameters Settings = new(2048, 64, false, 0, 1, 10, "chat", null);
    private static readonly PromptMessage[] Input = [new("user", "Bonjour")];

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Text_and_stream_agree_and_enforce_capabilities(bool mock)
    {
        using var http = new HttpClient(new ProviderTransport()) { BaseAddress = new("http://localhost/") };
        ITextGeneration provider = mock ? new ScriptedTextGeneration() : new OllamaTextGeneration(http, new());
        var model = Assert.Single(await provider.ListModelsAsync(default));
        var ordinary = await provider.GenerateAsync(model, Input, Settings, default);
        var deltas = new List<string>();
        var streamed = await provider.StreamAsync(model, Input, Settings, delta =>
        {
            deltas.Add(delta.Content);
            return Task.CompletedTask;
        }, false, default);
        Assert.Equal("Bonjour", ordinary.Content);
        Assert.Equal(["Bon", "jour"], deltas);
        Assert.Equal(ordinary.Content, streamed.Content);
        await Assert.ThrowsAsync<ChatModelException>(() => provider.StreamAsync(model, Input, Settings, _ => Task.CompletedTask, true, default));
        await Assert.ThrowsAsync<ChatModelException>(() => provider.GenerateAsync("absent", Input, Settings, default));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => provider.GenerateAsync(model, Input, Settings, new(true)));
    }

    [Fact]
    public async Task Interrupted_script_stops_at_last_acknowledged_delta()
    {
        var provider = new ScriptedTextGeneration(["a", "b", "c"], failAfter: 1);
        var deltas = new List<string>();
        await Assert.ThrowsAsync<ChatModelException>(() => provider.StreamAsync(ScriptedTextGeneration.Model, Input, Settings,
            d => { deltas.Add(d.Content); return Task.CompletedTask; }, false, default));
        Assert.Equal(["a"], deltas);
    }

    [Fact]
    public async Task Simulated_vectors_are_partitioned_by_model_and_cancel_before_work()
    {
        var provider = new DeterministicEmbeddings();
        var first = await provider.EmbedAsync("m", ["a", "a", "b"], default);
        Assert.Equal(first.Vectors[0], first.Vectors[1]);
        Assert.False(first.Vectors[0].SequenceEqual(first.Vectors[2]));
        Assert.NotEqual(await provider.IdentityAsync("m", default), await provider.IdentityAsync("n", default));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => provider.EmbedAsync("m", [], new(true)));
    }

    [Fact]
    public async Task Snapshot_never_returns_another_owner_or_a_removed_file()
    {
        var owner = Guid.NewGuid();
        var file = Guid.NewGuid();
        var chunk = new SourceChunk(file, "sample.txt", "sha", "", "text", 0, 3, 1, 1, "abc", ["type"], true);
        IProjectIndex index = new SnapshotIndex(owner, [chunk]);
        chunk.References[0] = "changed";
        Assert.Empty((await index.BuildAsync(Guid.NewGuid(), default)).Chunks);
        Assert.Empty((await index.BuildScopedAsync(owner, new HashSet<Guid>(), default)).Chunks);
        var returned = Assert.Single((await index.BuildScopedAsync(owner, new HashSet<Guid> { file }, default)).Chunks);
        Assert.Equal("type", returned.References[0]);
    }

    private sealed class ProviderTransport : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var text = request.RequestUri!.AbsolutePath switch
            {
                "/api/tags" => """{"models":[{"name":"alycia-mock"}]}""",
                "/api/show" => """{"capabilities":["completion"]}""",
                "/api/chat" => Reply(await request.Content!.ReadAsStringAsync(ct)),
                _ => throw new InvalidOperationException("Unexpected provider URL")
            };
            return new(HttpStatusCode.OK) { Content = new StringContent(text, Encoding.UTF8, "application/json") };
        }

        private static string Reply(string body)
        {
            using var json = JsonDocument.Parse(body);
            if (!json.RootElement.GetProperty("stream").GetBoolean())
                return """{"message":{"role":"assistant","content":"Bonjour"},"done":true,"done_reason":"stop"}""";
            return """{"message":{"role":"assistant","content":"Bon"},"done":false}""" + "\n"
                + """{"message":{"role":"assistant","content":"jour"},"done":true,"done_reason":"stop"}""" + "\n";
        }
    }
}
