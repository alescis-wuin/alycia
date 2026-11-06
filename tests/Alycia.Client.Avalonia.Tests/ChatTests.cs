using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Alycia.Client.Avalonia;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Alycia.Client.Avalonia.Tests;

public sealed class ChatTests
{
    [Fact]
    public async Task PreservesRequestIdAndReportsBackendErrors()
    {
        var id = Guid.NewGuid();
        var requestId = Guid.NewGuid();
        using var http = new HttpClient(new Handler(async (request, ct) =>
        {
            Assert.Equal($"/api/chat/conversations/{id}/messages", request.RequestUri!.AbsolutePath);
            using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
            Assert.Equal(requestId, json.RootElement.GetProperty("requestId").GetGuid());
            Assert.Equal("hello", json.RootElement.GetProperty("content").GetString());
            return Json(new { message = "Ollama indisponible" }, HttpStatusCode.ServiceUnavailable);
        }));
        var client = new ChatApiClient(http, "http://127.0.0.1:5187", TimeSpan.FromSeconds(2));
        var error = await Assert.ThrowsAsync<ChatApiException>(() => client.SendMessageAsync(id, requestId, "hello", default));
        Assert.Equal("Ollama indisponible", error.Message);
    }

    [Fact]
    public async Task RejectsNonlocalBackendBeforeSendingAnyMessage()
    {
        using var http = new HttpClient(new Handler((_, _) => throw new InvalidOperationException("Unexpected HTTP")));
        var client = new ChatApiClient(http, "https://example.com", TimeSpan.FromSeconds(2));
        await Assert.ThrowsAsync<ChatApiException>(() => client.ListAsync(default));
    }

    [Fact]
    public void ChatWindowCreatesRenamesSendsRetainsFailedDraftAndDeletes()
    {
        AppBuilder.Configure<App>().WithInterFont().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions
        {
            UseHeadlessDrawing = false
        }).SetupWithoutStarting();
        ConversationSummary? conversation = null;
        var messages = new List<ChatMessageItem>();
        var fail = false;
        using var http = new HttpClient(new Handler(async (request, ct) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/models"))
            {
                return Json(new[] { "demo" });
            }

            if (request.Method == HttpMethod.Get && path.EndsWith("/conversations"))
            {
                return Json(conversation is null ? Array.Empty<ConversationSummary>() : [conversation]);
            }

            if (request.Method == HttpMethod.Get)
            {
                return Json(new MessagePage(conversation!, messages.ToArray(), false));
            }

            if (request.Method == HttpMethod.Delete)
            {
                conversation = null;
                messages.Clear();
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }

            using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
            var root = json.RootElement;
            if (request.Method == HttpMethod.Patch)
            {
                conversation = conversation! with { Title = root.GetProperty("title").GetString()! };
                return Json(conversation);
            }

            if (path.EndsWith("/conversations"))
            {
                conversation = new(Guid.NewGuid(), root.GetProperty("title").GetString()!, "demo", "system",
                    DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, 0, 0);
                return Json(conversation, HttpStatusCode.Created);
            }

            if (fail)
            {
                return Json(new { message = "Ollama indisponible" }, HttpStatusCode.ServiceUnavailable);
            }

            var requestId = root.GetProperty("requestId").GetGuid();
            messages.Add(new(conversation!.Id, 1, requestId, "user", root.GetProperty("content").GetString()!, DateTimeOffset.UtcNow));
            messages.Add(new(conversation.Id, 2, requestId, "assistant", "Bonjour ! Je suis prête pour la démonstration.", DateTimeOffset.UtcNow));
            conversation = conversation with { MessageCount = 2 };
            return Json(new ChatSendResult(messages.ToArray(), 0, false));
        }));
        var window = new ChatWindow(new ChatApiClient(http, "http://127.0.0.1:5187", TimeSpan.FromSeconds(2)));
        try
        {
            window.Show();
            PumpUntil(() => window.FindControl<Button>("RefreshButton")!.IsEnabled);
            Assert.False(window.FindControl<Button>("SendButton")!.IsEnabled);
            window.FindControl<TextBox>("NewTitleInput")!.Text = "Démo locale";
            Click(window, "NewButton");
            Assert.NotNull(conversation);
            window.FindControl<TextBox>("TitleInput")!.Text = "Ma démonstration";
            Click(window, "RenameButton");
            Assert.Equal("Ma démonstration", conversation!.Title);
            window.FindControl<TextBox>("MessageInput")!.Text = "Bonjour Alycia";
            Click(window, "SendButton");
            Assert.Equal(2, messages.Count);
            Assert.Equal("", window.FindControl<TextBox>("MessageInput")!.Text);
            Assert.Equal(2, window.FindControl<ItemsControl>("MessageList")!.ItemCount);

            // Optional visual evidence from the actual Avalonia/Skia rendering.
            if (Environment.GetEnvironmentVariable("ALYCIA_UI_CAPTURE") is { Length: > 0 } screenshot)
            {
                Dispatcher.UIThread.RunJobs();
                using var frame = window.CaptureRenderedFrame();
                Assert.NotNull(frame);
                frame.Save(screenshot, global::Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
            }

            fail = true;
            window.FindControl<TextBox>("MessageInput")!.Text = "Conserver ce brouillon";
            Click(window, "SendButton");
            Assert.Equal("Conserver ce brouillon", window.FindControl<TextBox>("MessageInput")!.Text);
            Assert.Equal("Ollama indisponible", window.FindControl<TextBlock>("FeedbackText")!.Text);
            window.FindControl<Button>("DeleteButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            PumpUntil(() => window.OwnedWindows.Count > 0);
            var dialog = window.OwnedWindows.Single();
            var confirm = dialog.GetVisualDescendants().OfType<Button>()
                .Single(button => Equals(button.Content, "Supprimer définitivement"));
            confirm.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            PumpUntil(() => window.FindControl<Button>("RefreshButton")!.IsEnabled);
            Assert.Null(conversation);
            Assert.Equal(0, window.FindControl<ItemsControl>("MessageList")!.ItemCount);
        }
        finally
        {
            window.Close();
        }
    }

    private static void Click(Window window, string name)
    {
        window.FindControl<Button>(name)!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        PumpUntil(() => window.FindControl<Button>("RefreshButton")!.IsEnabled);
    }

    private static void PumpUntil(Func<bool> condition)
    {
        var timeout = DateTime.UtcNow.AddSeconds(5);
        while (!condition() && DateTime.UtcNow < timeout)
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(10);
        }

        Dispatcher.UIThread.RunJobs();
        Assert.True(condition(), "UI operation did not complete.");
    }

    private static HttpResponseMessage Json(object value, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = JsonContent.Create(value) };

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> action) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => action(request, ct);
    }
}
