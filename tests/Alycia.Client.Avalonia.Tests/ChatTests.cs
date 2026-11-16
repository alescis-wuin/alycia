using System.Net;
using System.Text;
using System.Net.Http.Json;
using System.Text.Json;
using Alycia.Client.Avalonia;
using Alycia.Modules.Chat.Application;
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
    public async Task SendsContainerTokenToValidatedLocalBackend()
    {
        using var http = new HttpClient(new Handler((request, _) =>
        {
            Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
            Assert.Equal("local-secret", request.Headers.Authorization.Parameter);
            return Task.FromResult(Json(Array.Empty<ConversationSummary>()));
        }));
        var client = new ChatApiClient(http, "http://127.0.0.1:5187", TimeSpan.FromSeconds(2), "local-secret");
        Assert.Empty(await client.ListAsync(default));
    }

    [Fact]
    public async Task RejectsNonlocalBackendBeforeSendingAnyMessage()
    {
        using var http = new HttpClient(new Handler((_, _) => throw new InvalidOperationException("Unexpected HTTP")));
        var client = new ChatApiClient(http, "https://example.com", TimeSpan.FromSeconds(2));
        await Assert.ThrowsAsync<ChatApiException>(() => client.ListAsync(default));
    }

    [Fact]
    public async Task UploadsRelativePathsAsMultipartAndDownloadsOriginalBytes()
    {
        var conversation = Guid.NewGuid();
        var batch = Guid.NewGuid();
        var file = Guid.NewGuid();
        byte[] payload = [0, 1, 255, 42];
        using var http = new HttpClient(new Handler(async (request, ct) =>
        {
            if (request.Method == HttpMethod.Post)
            {
                Assert.EndsWith($"/{conversation}/files/{batch}", request.RequestUri!.AbsolutePath);
                var multipart = Assert.IsType<MultipartFormDataContent>(request.Content);
                var part = Assert.Single(multipart);
                Assert.Equal("folder/file.bin", part.Headers.ContentDisposition!.FileNameStar);
                Assert.Equal(payload, await part.ReadAsByteArrayAsync(ct));
                return Json(new[] { new FileItem(file, "folder/file.bin", 4, "Conservé", 1, DateTimeOffset.UtcNow, null, "hash") });
            }
            Assert.EndsWith($"/{conversation}/files/{file}", request.RequestUri!.AbsolutePath);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(payload) };
        }));
        var client = new ChatApiClient(http, "http://127.0.0.1:5187", TimeSpan.FromSeconds(2));
        var imported = await client.UploadAsync(conversation, batch, [new("folder/file.bin", new MemoryStream(payload))], default);
        Assert.Equal(file, Assert.Single(imported).Id);
        using var output = new MemoryStream();
        await client.DownloadAsync(conversation, file, output, default);
        Assert.Equal(payload, output.ToArray());
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
        FileItem[] files = [];
        var fail = false;
        using var http = new HttpClient(new Handler(async (request, ct) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/models"))
            {
                return Json(new[] { "demo" });
            }

            if (request.Method == HttpMethod.Get && path.EndsWith("/search"))
            {
                return Json(new ConversationSearch(conversation is null ? [] : [conversation], false));
            }

            if (request.Method == HttpMethod.Get && path.EndsWith("/settings"))
                return Json(new SettingsReply(conversation!, new ChatOptions()));
            if (request.Method == HttpMethod.Get && path.EndsWith("/prompts"))
                return Json(Array.Empty<PromptItem>());
            if (request.Method == HttpMethod.Get)
            {
                return Json(new MessagePage(conversation!, messages.ToArray(), false, files));
            }

            if (request.Method == HttpMethod.Delete)
            {
                conversation = null;
                messages.Clear();
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }

            using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
            var root = json.RootElement;
            if (request.Method == HttpMethod.Put && path.EndsWith("/settings"))
            {
                conversation = conversation! with { SystemPrompt = root.GetProperty("systemPrompt").GetString()!,
                    ContextNotes = root.GetProperty("contextNotes").GetString()!, Revision = conversation.Revision + 1 };
                Assert.Equal(0.25, root.GetProperty("options").GetProperty("temperature").GetDouble());
                return Json(conversation);
            }
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
            var result = new ChatSendResult(messages.ToArray(), 0, false);
            var settings = new JsonSerializerOptions(JsonSerializerDefaults.Web);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(
                JsonSerializer.Serialize(new { type = "delta", content = "Bonjour !" }, settings) + "\n"
                + JsonSerializer.Serialize(new { type = "done", result }, settings) + "\n", Encoding.UTF8, "application/x-ndjson") };
        }));
        var stateDirectory = Path.Combine(Path.GetTempPath(), "alycia-ui-" + Guid.NewGuid().ToString("N"));
        var window = new ChatWindow(new ChatApiClient(http, "http://127.0.0.1:5187", TimeSpan.FromSeconds(2)), new DraftStore(stateDirectory));
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
            window.FindControl<Button>("SettingsButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            PumpUntil(() => window.OwnedWindows.Count > 0);
            var settingsDialog = window.OwnedWindows.Single();
            var fields = settingsDialog.GetVisualDescendants().OfType<TextBox>().ToArray();
            fields.Single(field => field.Text == "0.7").Text = "0.25";
            fields.Single(field => field.Text == "system").Text = "Updated system";
            settingsDialog.GetVisualDescendants().OfType<Button>().Single(button => Equals(button.Content, "Enregistrer"))
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            PumpUntil(() => window.FindControl<Button>("RefreshButton")!.IsEnabled);
            Assert.Equal("Updated system", conversation.SystemPrompt);
            window.FindControl<TextBox>("MessageInput")!.Text = "Bonjour Alycia";
            Click(window, "SendButton");
            Assert.Equal(2, messages.Count);
            Assert.Equal("", window.FindControl<TextBox>("MessageInput")!.Text);
            Assert.Equal(2, window.FindControl<ItemsControl>("MessageList")!.ItemCount);

            files = [new(Guid.NewGuid(), "projet/notes.md", 1024, "Texte extrait", 3, DateTimeOffset.UtcNow, null, "hash")];
            Click(window, "RefreshButton");
            Assert.Equal(3, window.FindControl<ItemsControl>("MessageList")!.ItemCount);
            Assert.True(window.FindControl<Button>("UploadButton")!.IsEnabled);
            Assert.True(window.FindControl<Button>("FolderButton")!.IsEnabled);
            Assert.Contains(window.GetVisualDescendants().OfType<Button>(), button => Equals(button.Content, "Télécharger") && button.IsVisible);

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
            if (Directory.Exists(stateDirectory)) Directory.Delete(stateDirectory, true);
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
