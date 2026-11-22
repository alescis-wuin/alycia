using Xunit.Abstractions;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Text;

namespace Alycia.Modules.Chat.Tests;

[Collection("Foreground indexing")]
public sealed class OllamaSchedulingTests(ITestOutputHelper output)
{
    [Fact]
    public async Task InteractiveStreamCanRunBeforeTheEntireAnnotationBacklog()
    {
        var measure = Environment.GetEnvironmentVariable("ALYCIA_MEASURE_OLLAMA_QUEUE") == "1";
        var samples = new List<Call[]>();
        // The optional first round warms JIT/JSON. The regression assertion uses order, never elapsed time.
        for (int round = measure ? -1 : 0; round < (measure ? 10 : 1); round++)
        {
            var delayMs = measure ? 30 : 0;
            using var watchdog = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var ct = watchdog.Token;
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var embedding = new Call("embedding", delayMs, release.Task);
            var annotations = Enumerable.Range(1, 3).Select(n => new Call($"annotation-{n}", delayMs)).ToArray();
            var chat = new Call("chat", delayMs);

            var encoding = embedding.RunAsync(ct);
            try
            {
                await embedding.InferenceStarted.Task.WaitAsync(ct);
                // Each adapter call reaches its first incomplete await while the only model slot is occupied.
                var queued = annotations.Select(a => a.RunAsync(ct)).ToArray();
                using (InferenceTestProvider.Foreground.PreferForeground())
                {
                    var interactive = chat.RunAsync(ct);
                    release.SetResult();
                    await interactive;
                }
                await Task.WhenAll(queued);
            }
            finally { release.TrySetResult(); await encoding; }

            Call[] calls = [embedding, .. annotations, chat];
            var ordered = calls.OrderBy(c => c.FirstHttp).ToArray();
            for (int i = 1; i < ordered.Length; i++)
                Assert.True(ordered[i - 1].LastReply <= ordered[i].FirstHttp, "Two simulated providers were active together.");
            if (round >= 0) samples.Add(calls);
        }

        foreach (var group in samples.SelectMany(s => s).GroupBy(c => c.Name))
        {
            var waits = Percentiles(group.Select(c => Elapsed(c.Submission, c.FirstHttp)));
            var active = Percentiles(group.Select(c => Elapsed(c.FirstHttp, c.LastReply)));
            var total = Percentiles(group.Select(c => Elapsed(c.Submission, c.Completed)));
            string first = group.Key == "chat" ? Percentiles(group.Select(c => Elapsed(c.Submission, c.FirstDelta))) : "-";
            output.WriteLine($"{group.Key} n={group.Count()} wait={waits} active={active} total={total} firstDelta={first} (p50/p95 ms)");
        }
        foreach (var calls in samples)
        {
            var chat = calls.Single(c => c.Name == "chat");
            int beforeChat = calls.Count(c => c.Name.StartsWith("annotation-", StringComparison.Ordinal) && c.FirstHttp < chat.FirstHttp);
            Assert.True(beforeChat <= 1, $"{beforeChat} already submitted annotations ran before the interactive stream.");
        }
    }

    [Fact]
    public async Task CancellationAtBothQueuesAndProviderFailureLeaveBothPermitsUsable()
    {
        using var watchdog = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var ct = watchdog.Token;
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var embedding = new Call("embedding", hold: release.Task);
        var encoding = embedding.RunAsync(ct);
        try
        {
            await embedding.InferenceStarted.Task.WaitAsync(ct);
            using var first = CancellationTokenSource.CreateLinkedTokenSource(ct);
            using var second = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var admitted = new Call("annotation-admitted");
            var pending = new Call("annotation-pending");
            var waitingForSlot = admitted.RunAsync(first.Token);
            var waitingForAdmission = pending.RunAsync(second.Token);
            var start = Stopwatch.GetTimestamp();
            await second.CancelAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waitingForAdmission);
            var middle = Stopwatch.GetTimestamp();
            await first.CancelAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waitingForSlot);
            output.WriteLine(FormattableString.Invariant(
                $"cancel pending/admitted: {Elapsed(start, middle):F2}/{Elapsed(middle, Stopwatch.GetTimestamp()):F2} ms"));
            Assert.Equal(0, admitted.FirstHttp);
            Assert.Equal(0, pending.FirstHttp);
        }
        finally { release.TrySetResult(); await encoding; }

        var refused = new Call("annotation-refused", status: HttpStatusCode.ServiceUnavailable);
        var error = await Assert.ThrowsAsync<ChatModelException>(() => refused.RunAsync(ct));
        Assert.Contains("503", error.Message);
        await new Call("annotation-retry").RunAsync(ct);
        await new Call("chat").RunAsync(ct);
    }

    private static double Elapsed(long start, long end) => Stopwatch.GetElapsedTime(start, end).TotalMilliseconds;

    private static string Percentiles(IEnumerable<double> values)
    {
        var sorted = values.Order().ToArray();
        double At(double fraction) => sorted[(int)Math.Ceiling(fraction * sorted.Length) - 1];
        return string.Create(CultureInfo.InvariantCulture, $"{At(.5):F2}/{At(.95):F2}");
    }

    private sealed class Call(string name, int delayMs = 0, Task? hold = null, HttpStatusCode status = HttpStatusCode.OK)
        : HttpMessageHandler
    {
        public string Name => name;
        public long Submission, FirstHttp, LastReply, FirstDelta, Completed;
        public TaskCompletionSource InferenceStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task RunAsync(CancellationToken ct)
        {
            using var http = new HttpClient(this) { BaseAddress = new Uri("http://127.0.0.1:11434/") };
            var model = InferenceTestProvider.Ollama(http, new());
            var settings = new ProviderSettings { CallPurpose = name == "chat" ? "chat" : "annotation", TimeoutSeconds = 5 };
            Submission = Stopwatch.GetTimestamp();
            if (name == "embedding")
                Assert.Equal(2, (await new OllamaEmbeddings(http, InferenceTestProvider.Scheduler).EmbedAsync("vectors", ["a", "b"], ct)).Vectors.Length);
            else if (name == "chat")
                Assert.Equal("ok", (await model.StreamAsync("local", [], settings.InferenceCall(), delta =>
                {
                    if (delta.Content.Length > 0) Interlocked.CompareExchange(ref FirstDelta, Stopwatch.GetTimestamp(), 0);
                    return Task.CompletedTask;
                }, ct)).Content);
            else
                Assert.Equal("ok", await model.GenerateAsync("local", [], settings.InferenceCall(), ct));
            Completed = Stopwatch.GetTimestamp();
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Interlocked.CompareExchange(ref FirstHttp, Stopwatch.GetTimestamp(), 0);
            switch (request.RequestUri!.AbsolutePath)
            {
                case "/api/tags": return Json("""{"models":[{"name":"local"}]}""");
                case "/api/show": return Json("""{"capabilities":["completion"]}""");
                case "/api/embed":
                case "/api/chat":
                    InferenceStarted.SetResult();
                    if (hold is not null) await hold.WaitAsync(ct);
                    if (delayMs > 0) await Task.Delay(delayMs, ct);
                    var reply = name switch
                    {
                        "embedding" => """{"embeddings":[[1,0],[0,1]]}""",
                        "chat" => """
                            {"done":false,"message":{"role":"assistant","content":"ok"}}
                            {"done":true,"done_reason":"stop"}
                            """,
                        _ => """{"done":true,"done_reason":"stop","message":{"role":"assistant","content":"ok"}}"""
                    };
                    LastReply = Stopwatch.GetTimestamp();
                    return Json(reply, status);
                default: throw new InvalidOperationException(request.RequestUri.AbsolutePath);
            }
        }

        private static HttpResponseMessage Json(string payload, HttpStatusCode code = HttpStatusCode.OK) =>
            new(code) { Content = new StringContent(payload, Encoding.UTF8, "application/json") };
    }
}
