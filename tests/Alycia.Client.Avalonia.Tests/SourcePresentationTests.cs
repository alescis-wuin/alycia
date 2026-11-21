using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Alycia.Contracts.Chat;

namespace Alycia.Client.Avalonia.Tests;

public sealed class SourcePresentationTests
{
    [Fact]
    public async Task DocumentBatchStopsAtConflictAndRefreshesTheActualRevision()
    {
        var owner = Guid.NewGuid();
        var project = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var chat = new ConversationSummary(owner, "test", "demo", "", now, now, 5, 0, WorkspaceId: project);
        var privateFile = new FileItem(Guid.NewGuid(), "Plan du projet", 20, "ok", 1, now, null, "sha");
        var shared = privateFile with { Id = Guid.NewGuid(), Name = "Données", WorkspaceId = project };
        var files = new[] { privateFile, shared, privateFile with { Id = Guid.NewGuid(), Name = "Annexe" } };
        var revisions = new List<int>();
        int reads = 0;
        using var http = new HttpClient(new Handler(async (request, ct) =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/settings"))
                return Json(new SettingsReply(chat with { Revision = reads++ == 0 ? 5 : 9 }, new()));
            if (request.Method == HttpMethod.Get) return Json(files);
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
            revisions.Add(body.RootElement.GetProperty("revision").GetInt32());
            Assert.Equal("pinned", body.RootElement.GetProperty("mode").GetString());
            return revisions.Count == 1 ? Json(files[0]) : Json(new { message = "révision périmée" }, HttpStatusCode.Conflict);
        }));
        var list = await DocumentList.LoadAsync(Client(http), owner, false, default);
        Assert.Equal(privateFile, Assert.Single(list.Filter("pLAN")));
        Assert.True(list.CanRestore([shared]));
        Assert.True(list.CanShare([privateFile]));
        Assert.False(list.CanShare([shared, privateFile]));
        var change = await list.ChangeModesAsync(files, "pinned", default);
        Assert.Equal([5, 6], revisions);
        Assert.Equal(1, change.Completed);
        Assert.Equal(9, change.Conversation!.Revision);
        Assert.Equal("révision périmée", change.Error);
    }

    [Fact]
    public async Task IndexRefreshSkipsConcurrentReadsAndIgnoresThePreviousScopeEvenWithTheSameId()
    {
        var id = Guid.NewGuid();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        using var http = new HttpClient(new Handler(async (request, ct) =>
        {
            if (Interlocked.Increment(ref calls) == 1)
            {
                using var registration = ct.Register(() => cancelled.TrySetResult());
                started.SetResult();
                await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));
                return Json(new { message = "ancien propriétaire" }, HttpStatusCode.Conflict);
            }
            Assert.Contains("project=true", request.RequestUri!.Query);
            return Json(new[] { new EmbeddingProgress(id, true, "running", 2, 4, "index", "demo", DateTimeOffset.UtcNow) });
        }));
        var monitor = new IndexingMonitor(Client(http));
        var displayed = new List<IndexingStatus>();
        monitor.Select(id, false);
        var refresh = monitor.RefreshAsync(() => null, displayed.Add, default);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await monitor.RefreshAsync(() => null, displayed.Add, default);
        Assert.Equal(1, calls);
        monitor.Select(id, true);
        await refresh.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Empty(displayed);
        await monitor.RefreshAsync(() => null, displayed.Add, default);
        var status = Assert.Single(displayed);
        Assert.True(status.Running);
        Assert.Equal(50, status.Percent);
        using var lifetime = new CancellationTokenSource();
        var poll = monitor.PollAsync(() => null, displayed.Add, lifetime.Token);
        lifetime.Cancel();
        await poll.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task HistorySourceRequiresMatchingOwnerSequenceAndHash()
    {
        var owner = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var chat = new ConversationSummary(owner, "test", "demo", "", now, now, 1, 2);
        var message = new ChatMessageItem(owner, 2, Guid.NewGuid(), "assistant", "Réponse originale.", now);
        var page = new MessagePage(chat, [message], false);
        using var http = new HttpClient(new Handler((request, _) =>
        {
            Assert.Contains("before=3", request.RequestUri!.Query);
            return Task.FromResult(Json(page));
        }));
        var browser = new KnowledgeBrowser(Client(http));
        var reference = "H2:" + MemoryTransfer.Hash(message.Content)[..12];
        Assert.Contains(message.Content, await browser.ReadOriginalAsync(owner, 2, reference, default));
        Assert.Null(await browser.ReadOriginalAsync(owner, 2, reference + "altéré", default));
        Assert.Null(await browser.ReadOriginalAsync(Guid.NewGuid(), 2, reference, default));
    }

    private static ChatApiClient Client(HttpClient http) => new(http, "http://127.0.0.1:5187", TimeSpan.FromSeconds(5));
    private static HttpResponseMessage Json<T>(T value, HttpStatusCode code = HttpStatusCode.OK) =>
        new(code) { Content = JsonContent.Create(value) };
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handle) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => handle(request, ct);
    }
}
