using Alycia.Contracts.Chat;
using System.Net;
using System.Text;
using System.Text.Json;
using Alycia.Modules.Inference;
using Alycia.Modules.Testing;

namespace Alycia.Modules.Inference.Tests;

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
        ITextGeneration provider = mock ? new ScriptedTextGeneration() : new OllamaTextGeneration(http, new(), new());
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
        var emptyOwner = Guid.NewGuid();
        IProjectIndex index = new SnapshotIndex(owner, [chunk], emptyOwner);
        chunk.References[0] = "changed";
        Assert.Empty((await index.BuildAsync(emptyOwner, default)).Chunks);
        await Assert.ThrowsAsync<ConversationNotFoundException>(() => index.BuildAsync(Guid.NewGuid(), default));
        Assert.Empty((await index.BuildScopedAsync(owner, new HashSet<Guid>(), default)).Chunks);
        var returned = Assert.Single((await index.BuildScopedAsync(owner, new HashSet<Guid> { file }, default)).Chunks);
        Assert.Equal("type", returned.References[0]);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Vector_provider_preserves_row_order_identity_and_cancellation(bool mock)
    {
        using var http = new HttpClient(new ProviderTransport()) { BaseAddress = new("http://localhost/") };
        ILocalEmbeddings provider = mock
            ? new DeterministicEmbeddings(new Dictionary<string, string> { ["embedding"] = "fixture-v1" },
                (_, text) => text == "first" ? [1f, 0f] : [0f, 1f])
            : new OllamaEmbeddings(http, new());
        var identity = await provider.IdentityAsync("embedding", default);
        Assert.Equal(identity, await provider.IdentityAsync("embedding", default));
        await Assert.ThrowsAsync<ArgumentException>(() => provider.IdentityAsync("missing", default));
        var batch = await provider.EmbedAsync("embedding", ["second", "first"], default);
        Assert.Equal([0f, 1f], batch.Vectors[0]);
        Assert.Equal([1f, 0f], batch.Vectors[1]);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => provider.EmbedAsync("embedding", ["first"], new(true)));
    }

    [Fact]
    public async Task A_waiting_delta_can_be_cancelled_without_waiting_for_a_clock()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pause = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var provider = new ScriptedTextGeneration(beforeDelta: (_, ct) => { started.SetResult(); return pause.Task.WaitAsync(ct); });
        using var stop = new CancellationTokenSource();
        var deltas = new List<string>();
        var pending = provider.StreamAsync(ScriptedTextGeneration.Model, Input, Settings,
            d => { deltas.Add(d.Content); return Task.CompletedTask; }, false, stop.Token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        Assert.Empty(deltas);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Queued_request_keeps_its_messages_and_tool_arguments(bool streaming)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        string? observed = null;
        using var http = new HttpClient(new ProviderTransport(release.Task, body => observed = body))
            { BaseAddress = new("http://localhost/") };
        ITextGeneration provider = new OllamaTextGeneration(http, new(), new());
        using var arguments = JsonDocument.Parse("""{"path":"initial"}""");
        ModelToolCall[] calls = [new(new("read", arguments.RootElement))];
        PromptMessage[] messages = [new("assistant", "initial", calls), new("user", "Bonjour")];
        var pending = streaming
            ? provider.StreamAsync("alycia-mock", messages, Settings, _ => Task.CompletedTask, false, deadline.Token)
            : provider.GenerateAsync("alycia-mock", messages, Settings, deadline.Token);
        calls[0] = new(new("changed", default));
        messages[1] = new("user", "changed");
        arguments.Dispose();
        release.SetResult();

        await pending;
        using var request = JsonDocument.Parse(Assert.IsType<string>(observed));
        var sent = request.RootElement.GetProperty("messages");
        Assert.Equal("Bonjour", sent[1].GetProperty("content").GetString());
        var call = sent[0].GetProperty("tool_calls")[0].GetProperty("function");
        Assert.Equal("read", call.GetProperty("name").GetString());
        Assert.Equal("initial", call.GetProperty("arguments").GetProperty("path").GetString());
    }

    [Fact]
    public async Task Queued_embedding_preserves_the_text_and_row_count_supplied_by_the_caller()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var transport = new BlockingEmbeddings();
        using var http = new HttpClient(transport) { BaseAddress = new Uri("http://localhost/") };
        var adapter = new OllamaEmbeddings(http, new InferenceScheduler());
        var first = adapter.EmbedAsync("fixture", ["active"], deadline.Token);
        await transport.Entered.Task.WaitAsync(deadline.Token);
        var rows = new List<string> { "initial" };
        var queued = adapter.EmbedAsync("fixture", rows, deadline.Token);
        rows[0] = "modified";
        rows.Add("extra");
        transport.Release.SetResult();
        await first;
        var result = await queued;
        Assert.Equal(["initial"], transport.Seen);
        Assert.Single(result.Vectors);
    }

    private sealed class BlockingEmbeddings : HttpMessageHandler
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public string[] Seen { get; private set; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
            Seen = json.RootElement.GetProperty("input").Deserialize<string[]>()!;
            if (Entered.TrySetResult()) await Release.Task.WaitAsync(ct);
            var vectors = Seen.Select(_ => new[] { .5 }).ToArray();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(new { embeddings = vectors }))
            };
        }
    }

    private sealed class ProviderTransport(Task? gate = null, Action<string>? observe = null) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (gate is not null && request.RequestUri!.AbsolutePath == "/api/tags") await gate.WaitAsync(ct);
            if (observe is not null && request.RequestUri!.AbsolutePath == "/api/chat")
                observe(await request.Content!.ReadAsStringAsync(ct));
            var text = request.RequestUri!.AbsolutePath switch
            {
                "/api/tags" => """{"models":[{"name":"alycia-mock"},{"name":"embedding","digest":"fixture-v1"}]}""",
                "/api/show" => (await request.Content!.ReadAsStringAsync(ct)).Contains("\"embedding\"", StringComparison.Ordinal)
                    ? """{"capabilities":["embedding"]}""" : """{"capabilities":["completion"]}""",
                "/api/embed" => Vectors(await request.Content!.ReadAsStringAsync(ct)),
                "/api/chat" => Reply(await request.Content!.ReadAsStringAsync(ct)),
                _ => throw new InvalidOperationException("Unexpected provider URL")
            };
            return new(HttpStatusCode.OK) { Content = new StringContent(text, Encoding.UTF8, "application/json") };
        }

        private static string Vectors(string body)
        {
            using var json = JsonDocument.Parse(body);
            var rows = json.RootElement.GetProperty("input").EnumerateArray()
                .Select(item => item.GetString() == "first" ? new[] { 1f, 0f } : new[] { 0f, 1f });
            return JsonSerializer.Serialize(new { embeddings = rows });
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
