using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Alycia.Client.Avalonia.Presentation.Conversations;
using Alycia.Client.Avalonia.Presentation.Navigation;
using Alycia.Contracts.Chat;

namespace Alycia.Client.Avalonia.Tests;

public sealed class ConversationPresentationTests
{
    [Fact]
    public async Task LostAcknowledgementKeepsTheSubmittedIdUntilTheRetryIsConfirmed()
    {
        var directory = Directory.CreateTempSubdirectory("alycia-composer-");
        try
        {
            var owner = Chat();
            var other = Chat();
            var composer = new ConversationComposer(new(directory.FullName));
            composer.Edit(other.Id, "Autre brouillon");
            composer.Edit(owner.Id, " Question ");
            var original = composer.Begin(owner, " Question ")!;
            var calls = 0;
            using var http = new HttpClient(new Handler(async (request, ct) =>
            {
                using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
                Assert.Equal(original.Draft.RequestId, body.RootElement.GetProperty("requestId").GetGuid());
                Assert.Equal("Question", body.RootElement.GetProperty("content").GetString());
                Assert.Equal(owner.Revision, body.RootElement.GetProperty("revision").GetInt32());
                var line = ++calls == 1 ? """{"type":"delta","content":"Provisoire"}""" : """{"type":"done","result":{"messages":[],"replayed":true}}""";
                return new(HttpStatusCode.OK) { Content = new StringContent(line + "\n", Encoding.UTF8, "application/x-ndjson") };
            }));
            var client = new ChatApiClient(http, "http://127.0.0.1", TimeSpan.FromSeconds(2));
            await Assert.ThrowsAsync<ChatApiException>(() => composer.SendAsync(client, delta => { original.Append(delta); return Task.CompletedTask; }, default));
            Assert.False(original.Confirmed);
            Assert.Equal("Provisoire", original.Output);
            Assert.Null(composer.RestoreIfEmpty(other.Id, ""));
            Assert.Equal("Question", composer.RestoreIfEmpty(owner.Id, ""));
            composer.End();

            var reopened = new ConversationComposer(new(directory.FullName));
            var retry = reopened.Begin(owner, reopened.TextFor(owner.Id))!;
            Assert.Equal(original.Draft.RequestId, retry.Draft.RequestId);
            Assert.True((await reopened.SendAsync(client, _ => Task.CompletedTask, default)).Replayed);
            Assert.True(retry.Confirmed);
            Assert.Null(reopened.RestoreIfEmpty(owner.Id, ""));
            var stored = new DraftStore(directory.FullName);
            Assert.Null(stored.Get(owner.Id));
            Assert.Equal("Autre brouillon", stored.Get(other.Id)!.Text);
        }
        finally { directory.Delete(true); }
    }

    [Fact]
    public void EarlierPageKeepsFilesBeforeTheirAnchoredMessagesAndPreservesTheBoundCollection()
    {
        var owner = Chat();
        var timeline = new ConversationTimeline();
        var bound = timeline.Messages;
        ChatMessageItem Message(int seq, string role) => new(owner.Id, seq, Guid.NewGuid(), role, role, DateTimeOffset.UnixEpoch);
        FileItem File(string name, int anchor, int seconds) => new(Guid.NewGuid(), name, 1, "ok", anchor, DateTimeOffset.UnixEpoch.AddSeconds(seconds), null, "sha");
        timeline.Replace(new(owner, [Message(3, "user"), Message(4, "assistant")], true, [File("later", 3, 1)]));
        timeline.Prepend(new(owner, [Message(1, "user"), Message(2, "assistant")], false, [File("second", 1, 2), File("first", 1, 1)]));
        Assert.Same(bound, timeline.Messages);
        Assert.Equal(1, timeline.OldestSequence);
        Assert.Equal(new[] { "file", "file", "user", "assistant", "file", "user", "assistant" }, bound.Select(m => m.Role));
        Assert.Contains("first", bound[0].Content);
        Assert.Contains("second", bound[1].Content);
        timeline.Clear();
        Assert.Empty(bound);
        Assert.Null(timeline.OldestSequence);
    }

    [Fact]
    public async Task AConversationOutsideTheFilterIsRetainedOnlyInTheSelectedProject()
    {
        var project = new ProjectChoice(Guid.NewGuid(), "Actif");
        var visible = Chat(project.Id);
        var hidden = Chat(project.Id);
        var foreign = Chat(Guid.NewGuid());
        using var http = new HttpClient(new Handler((request, _) =>
        {
            var uri = request.RequestUri!;
            object body = uri.AbsolutePath switch
            {
                "/api/chat/projects" => new[] { project },
                "/api/chat/search" when uri.Query.Contains($"workspaceId={project.Id}") => new ConversationSearch([visible], false),
                _ when uri.AbsolutePath.Contains(hidden.Id.ToString()) => new SettingsReply(hidden, new()),
                _ when uri.AbsolutePath.Contains(foreign.Id.ToString()) => new SettingsReply(foreign, new()),
                _ => throw new InvalidOperationException(uri.AbsolutePath)
            };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(body) });
        }));
        var navigation = new ProjectNavigation(new(http, "http://127.0.0.1", TimeSpan.FromSeconds(2)));
        await navigation.RefreshProjectsAsync(project.Id, default);
        var page = await navigation.SearchAsync("filtre", hidden.Id, default);
        Assert.Equal(new[] { hidden.Id, visible.Id }, page!.Items.Select(c => c.Id));
        Assert.Equal(hidden.Id, navigation.Conversation!.Id);
        var foreignPage = await navigation.SearchAsync("filtre", foreign.Id, default);
        Assert.Equal(visible.Id, Assert.Single(foreignPage!.Items).Id);
        Assert.Equal(visible.Id, navigation.Conversation!.Id);
        await navigation.RefreshProjectsAsync(Guid.NewGuid(), default);
        Assert.Equal(ProjectNavigation.Outside, navigation.Project);
    }

    [Fact]
    public async Task LateSettingsForAFilteredConversationCannotSelectItInAnotherProject()
    {
        var project = new ProjectChoice(Guid.NewGuid(), "A");
        var next = new ProjectChoice(Guid.NewGuid(), "B");
        var hidden = Chat(project.Id);
        var current = Chat(next.Id);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var reply = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var http = new HttpClient(new Handler((request, _) =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/search"))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new ConversationSearch([], false)) });
            Assert.Contains(hidden.Id.ToString(), request.RequestUri.AbsolutePath);
            started.SetResult();
            return reply.Task;
        }));
        var navigation = new ProjectNavigation(new(http, "http://127.0.0.1", TimeSpan.FromSeconds(5))) { Project = project };
        var pending = navigation.SearchAsync("filtre A", hidden.Id, default);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        navigation.Project = next;
        navigation.Conversation = current;
        reply.SetResult(new(HttpStatusCode.OK) { Content = JsonContent.Create(new SettingsReply(hidden, new())) });
        Assert.Null(await pending);
        Assert.Equal(current, navigation.Conversation);
        Assert.Empty(navigation.SearchQuery);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RevisitingTheSameConversationDiscardsItsEarlierHistoryPage(bool failedRead)
    {
        var first = new ProjectChoice(Guid.NewGuid(), "A");
        var second = new ProjectChoice(Guid.NewGuid(), "B");
        var chat = Chat(first.Id);
        var other = Chat(second.Id);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var delayed = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var http = new HttpClient(new Handler((request, _) =>
        {
            if (request.RequestUri!.Query == "?before=10")
            {
                Assert.Contains(chat.Id.ToString(), request.RequestUri.AbsolutePath);
                started.SetResult();
                return delayed.Task;
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new MessagePage(chat, [], false)) });
        }));
        var navigation = new ProjectNavigation(new(http, "http://127.0.0.1", TimeSpan.FromSeconds(5)))
        {
            Project = first,
            Conversation = chat
        };
        var oldRead = navigation.ReadMessagesAsync(10, default);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        navigation.Project = second;
        Assert.Null(navigation.Conversation);
        navigation.Conversation = other;
        navigation.Project = first;
        navigation.Conversation = chat;
        delayed.SetResult(failedRead
            ? new(HttpStatusCode.Conflict) { Content = JsonContent.Create(new { message = "lecture périmée" }) }
            : new(HttpStatusCode.OK) { Content = JsonContent.Create(new MessagePage(chat, [], true)) });
        Assert.Null(await oldRead);
        Assert.Equal(chat.Id, (await navigation.ReadMessagesAsync(null, default))!.Conversation.Id);
    }

    private static ConversationSummary Chat(Guid? project = null) =>
        new(Guid.NewGuid(), "Chat", "demo", "", DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, 2, 0, WorkspaceId: project);

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => respond(request, ct);
    }
}
