using System.Net;
using System.Text;
using System.Text.Json;
using Alycia.Modules.Chat.Application;
using Alycia.Modules.Chat.Domain;
using Alycia.Modules.Chat.Infrastructure;

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
        var reply = await ChatFixture.Ollama(http, new()).StreamAsync("demo", [new("user", "hi")],
            new ChatOptions() { Temperature = 0.1 }.InferenceCall(), delta => { deltas.Add(delta); return Task.CompletedTask; }, default);
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
        await Assert.ThrowsAsync<ChatModelException>(() => ChatFixture.Ollama(http, new())
            .StreamAsync("demo", [], new ChatOptions().InferenceCall(), _ => Task.CompletedTask, default));
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
        await Assert.ThrowsAsync<ChatModelException>(() => ChatFixture.Ollama(http, new())
            .StreamAsync("demo", [], new ChatOptions().InferenceCall(), _ => Task.CompletedTask, default));
        // Explicit bounded mode remains available, including for summarization.
        var bounded = await ChatFixture.Ollama(http, new()).StreamAsync("demo", [],
            new ChatOptions() { GenerateUntilStop = false }.InferenceCall(), _ => Task.CompletedTask, default);
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
        var reply = await ChatFixture.Ollama(http, new()).StreamAsync("demo", [],
            new ChatOptions() { TimeoutSeconds = 1 }.InferenceCall(), _ => Task.CompletedTask, default);
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

    [Fact]
    public async Task ParserBoundsAnUnterminatedLine()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(new string('x', 100)));
        await Assert.ThrowsAsync<InvalidDataException>(async () =>
        {
            await foreach (var line in BoundedLines.ReadAsync(stream, default, 64)) { }
        });
    }

    [Fact]
    public async Task PartialFailureNeverCommitsAndRetryRemainsIdempotent()
    {
        var repository = new MemoryRepository();
        var model = new StreamingModel { Fail = true };
        var service = ChatFixture.Create(repository, model, new());
        var id = repository.Conversation.Id;
        var request = Guid.NewGuid();
        var received = new List<ModelDelta>();
        await Assert.ThrowsAsync<ChatModelException>(() => service.StreamAsync(id, request, "hello", 0,
            delta => { received.Add(delta); return Task.CompletedTask; }, default));
        Assert.NotEmpty(received);
        Assert.Empty(repository.Messages);
        Assert.Equal(0, repository.Conversation.MessageCount);
        model.Fail = false;
        var result = await service.StreamAsync(id, request, "hello", 0, _ => Task.CompletedTask, default);
        Assert.Equal(2, result.Messages.Count);
        var replay = await service.StreamAsync(id, request, "hello", 0,
            _ => throw new Exception("Replayed generation must not run"), default);
        Assert.True(replay.Replayed);
        Assert.Equal(2, repository.Messages.Count);
    }

    [Fact]
    public async Task CancellationAfterDeltaDoesNotPersistAndReleasesGate()
    {
        var repository = new MemoryRepository();
        var service = ChatFixture.Create(repository, new StreamingModel(), new());
        using var cancel = new CancellationTokenSource();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.StreamAsync(repository.Conversation.Id,
            Guid.NewGuid(), "hello", 0, _ => { cancel.Cancel(); return Task.CompletedTask; }, cancel.Token));
        Assert.Empty(repository.Messages);
        using var lease = ChatFixture.Gate.Enter(repository.Conversation.Id);
        Assert.Throws<ChatConflictException>(() => ChatFixture.Gate.Enter(repository.Conversation.Id));
    }

    private sealed class StreamingModel : IChatModel
    {
        public bool Fail { get; set; }
        public Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<string>>(["demo"]);
        public Task<string> GenerateAsync(string model, IReadOnlyList<PromptMessage> messages, CancellationToken ct) => Task.FromResult("reply");
        public async Task<ModelReply> StreamAsync(string model, IReadOnlyList<PromptMessage> messages, InferenceCall settings,
            Func<ModelDelta, Task> onDelta, CancellationToken ct)
        {
            await onDelta(new("partial"));
            if (Fail) throw new ChatModelException("Interrupted");
            ct.ThrowIfCancellationRequested();
            return new("reply");
        }
    }

    private sealed class MemoryRepository : ITestConversationRepository
    {
        public Conversation Conversation { get; } = Conversation.Create("test", "demo", "");
        public List<ChatMessage> Messages { get; } = [];
        public Task<IReadOnlyList<Attachment>> FilesAsync(Guid id, CancellationToken ct) => Task.FromResult<IReadOnlyList<Attachment>>([]);
        public Task<IReadOnlyList<Conversation>> ListAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<Conversation>>([Conversation]);
        public Task<Conversation?> FindAsync(Guid id, CancellationToken ct) => Task.FromResult<Conversation?>(Conversation);
        public Task AddAsync(Conversation c, CancellationToken ct) => Task.CompletedTask;
        public Task SaveAsync(CancellationToken ct) => Task.CompletedTask;
        public Task DeleteAsync(Conversation c, CancellationToken ct) => Task.CompletedTask;
        public Task<IReadOnlyList<ChatMessage>> MessagesAsync(Guid id, int limit, int? before, CancellationToken ct) => Task.FromResult<IReadOnlyList<ChatMessage>>(Messages);
        public Task<IReadOnlyList<ChatMessage>> FindTurnAsync(Guid id, Guid requestId, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<ChatMessage>>(Messages.Where(m => m.RequestId == requestId).ToArray());
        public Task AppendTurnAsync(Conversation c, ChatMessage user, ChatMessage assistant, CancellationToken ct)
        { Messages.AddRange([user, assistant]); return Task.CompletedTask; }
    }

    private static HttpResponseMessage Response(string text) => new(HttpStatusCode.OK)
    { Content = new StringContent(text, Encoding.UTF8, "application/x-ndjson") };
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> action) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => action(request, ct); }
}
