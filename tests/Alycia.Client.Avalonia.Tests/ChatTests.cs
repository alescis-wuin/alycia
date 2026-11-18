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
using Avalonia.Input;
using Avalonia.Input.Raw;
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UploadsRelativePathsAsMultipartAndDownloadsOriginalBytes(bool withMetadata)
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
                Assert.Equal(withMetadata ? 2 : 1, multipart.Count());
                var part = Assert.Single(multipart, p => p.Headers.ContentDisposition!.FileNameStar is not null);
                Assert.Equal("folder/file.bin", part.Headers.ContentDisposition!.FileNameStar);
                Assert.Equal(payload, await part.ReadAsByteArrayAsync(ct));
                if (withMetadata)
                {
                    var metadata = Assert.Single(multipart, p => p.Headers.ContentDisposition!.FileNameStar is null);
                    using var json = JsonDocument.Parse(await metadata.ReadAsStringAsync(ct));
                    var item = Assert.Single(json.RootElement.EnumerateArray());
                    Assert.Equal("folder/file.bin", item.GetProperty("name").GetString());
                    Assert.Equal(new DateTimeOffset(2026, 11, 1, 12, 0, 0, TimeSpan.Zero), item.GetProperty("createdAt").GetDateTimeOffset());
                    Assert.Equal(JsonValueKind.Null, item.GetProperty("modifiedAt").ValueKind);
                }
                return Json(new[] { new FileItem(file, "folder/file.bin", 4, "Conservé", 1, DateTimeOffset.UtcNow, null, "hash") });
            }
            Assert.EndsWith($"/{conversation}/files/{file}", request.RequestUri!.AbsolutePath);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(payload) };
        }));
        var client = new ChatApiClient(http, "http://127.0.0.1:5187", TimeSpan.FromSeconds(2));
        var imported = await client.UploadAsync(conversation, batch, [new("folder/file.bin", new MemoryStream(payload),
            withMetadata ? new DateTimeOffset(2026, 11, 1, 12, 0, 0, TimeSpan.Zero) : null)], default);
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
        MarkdownChecks.Verify();
        ConversationSummary? conversation = null;
        var messages = new List<ChatMessageItem>();
        FileItem[] files = [];
        var citedId = Guid.NewGuid();
        string citedHash = new string('a', 64);
        var fail = false;
        Action? beforeFailure = null;
        using var http = new HttpClient(new Handler(async (request, ct) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/projects")) return Json(Array.Empty<ProjectChoice>());
            if (path.EndsWith("/preview"))
            {
                Assert.Contains("sha=" + citedHash, request.RequestUri!.Query);
                Assert.Contains("start=2&end=9", request.RequestUri.Query);
                return Json(new SourcePreview("src/A.cs", citedHash, "passage", 2, 9, true));
            }
            if (path.EndsWith("/models"))
            {
                return Json(new[] { "demo", "other" });
            }

            if (request.Method == HttpMethod.Get && path.EndsWith("/search"))
            {
                return Json(new ConversationSearch(conversation is null ? [] : [conversation], false));
            }

            if (request.Method == HttpMethod.Get && path.EndsWith("/settings"))
                return Json(new SettingsReply(conversation!, new ChatOptions()));
            if (request.Method == HttpMethod.Get && path.EndsWith("/tools"))
                return Json(new ProjectToolsInfo(true, true, 24, ["project_list", "project_read", "project_search"], "2025-11-25"));
            if (request.Method == HttpMethod.Get && path.EndsWith("/files")) return Json(files);
            if (request.Method == HttpMethod.Get && path.EndsWith("/analyses")) return Json(Array.Empty<AnalysisItem>());
            if (request.Method == HttpMethod.Get && path.EndsWith("/analysis-runs"))
                return Json(new[] { new AnalysisRunItem(Guid.NewGuid(), null, 2048, "cancelled", 4, 2, 0, DateTimeOffset.UtcNow) });
            if (request.Method == HttpMethod.Get && path.EndsWith("/prompts"))
                return Json(Array.Empty<PromptItem>());
            if (request.Method == HttpMethod.Get)
            {
                int before = int.TryParse(request.RequestUri!.Query.Replace("?before=", ""), out var bound) ? bound : int.MaxValue;
                var eligible = messages.Where(m => m.Sequence < before).ToArray();
                return Json(new MessagePage(conversation!, eligible.TakeLast(100).ToArray(), eligible.Length > 100, files));
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
                conversation = conversation! with
                {
                    Model = root.GetProperty("model").GetString()!,
                    SystemPrompt = root.GetProperty("systemPrompt").GetString()!,
                    ContextNotes = root.GetProperty("contextNotes").GetString()!,
                    Revision = conversation.Revision + 1
                };
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
                beforeFailure?.Invoke();
                return Json(new { message = "Ollama indisponible" }, HttpStatusCode.ServiceUnavailable);
            }

            var requestId = root.GetProperty("requestId").GetGuid();
            messages.Add(new(conversation!.Id, 1, requestId, "user", root.GetProperty("content").GetString()!, DateTimeOffset.UtcNow));
            messages.Add(new(conversation.Id, 2, requestId, "assistant", "## :rocket: Votre atelier de développement\n\n👩🏽‍💻 🇫🇷 🦊 ❤️ · Je peux vous aider à **concevoir**, tester et améliorer votre projet.\n\n- ✅ Contexte protégé &amp; mémoire récursive\n- 📁 Inventaire vérifiable des sources\n\n```python\ndef saluer(nom):\n    return f\"Bonjour {nom}\"\n```\n\n| Étape | État |\n|---|---|\n| Sources | Prêtes |\n| Tests | À lancer |", DateTimeOffset.UtcNow));
            conversation = conversation with { MessageCount = 2 };
            messages[^1] = messages[^1] with
            {
                DiagnosticsJson = JsonSerializer.Serialize(new TurnDiagnostics(
                    new GenerationUsage([new("chat", 100, 200, new(100, 20, null, null, "stop"))], "demo:fixture", "UTF-8 conservateur", 100, 20, 200, true),
                    Citations: new("unknown_references", [new("Sabcdef123456", citedId, "src/A.cs", citedHash, 1, 1, 2, 9, "initial", true)], [], ["S000000000000"])), ProjectToolCatalog.Json),
                ToolTraceJson = JsonSerializer.Serialize(new[]
                { new ToolObservation(1, "project_read", "{}", true, 100, new("src/A.cs", new string('a', 64), 0, null, true)) }, ProjectToolCatalog.Json)
            };
            var result = new ChatSendResult(messages.ToArray(), 0, false);
            var settings = new JsonSerializerOptions(JsonSerializerDefaults.Web);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                JsonSerializer.Serialize(new { type = "delta", content = "Bonjour !" }, settings) + "\n"
                + JsonSerializer.Serialize(new { type = "done", result }, settings) + "\n", Encoding.UTF8, "application/x-ndjson")
            };
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
            window.FindControl<ComboBox>("ModelSelector")!.SelectedItem = "other";
            Assert.Equal("demo", conversation.Model);
            Assert.Contains("demo", window.FindControl<Button>("ActiveModelButton")!.Content!.ToString());
            window.FindControl<Button>("ActiveModelButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            PumpUntil(() => window.OwnedWindows.Count > 0);
            var settingsDialog = window.OwnedWindows.Single();
            var conversationModel = settingsDialog.GetVisualDescendants().OfType<ComboBox>()
                .Single(box => box.Name == "ConversationModelSelector");
            Assert.Equal("demo", conversationModel.SelectedItem);
            conversationModel.SelectedItem = "other";
            var advanced = settingsDialog.GetVisualDescendants().OfType<Expander>().Single(e => Equals(e.Header, "Avancé"));
            Assert.False(advanced.IsExpanded);
            advanced.IsExpanded = true;
            Dispatcher.UIThread.RunJobs();
            var fields = settingsDialog.GetVisualDescendants().OfType<TextBox>().ToArray();
            fields.Single(field => field.Text == "0.7").Text = "invalide";
            settingsDialog.GetVisualDescendants().OfType<Button>().Single(button => Equals(button.Content, "Enregistrer"))
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Contains(settingsDialog, window.OwnedWindows);
            Assert.Contains(settingsDialog.GetVisualDescendants().OfType<TextBlock>(), text => text.Text?.Contains("numérique est invalide") == true);
            fields.Single(field => field.Text == "invalide").Text = "0,25";
            fields.Single(field => field.Text == "system").Text = "Updated system";
            settingsDialog.GetVisualDescendants().OfType<Button>().Single(button => Equals(button.Content, "Enregistrer"))
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            PumpUntil(() => window.FindControl<Button>("RefreshButton")!.IsEnabled);
            Assert.Equal("Updated system", conversation.SystemPrompt);
            Assert.Equal("other", conversation.Model);
            Assert.Contains("other", window.FindControl<Button>("ActiveModelButton")!.Content!.ToString());
            window.FindControl<TextBox>("MessageInput")!.Text = "Bonjour Alycia";
            Click(window, "SendButton");
            Assert.Equal(2, messages.Count);
            Assert.True(window.FindControl<Button>("AnalysisButton")!.IsEnabled);
            Assert.True(messages[^1].HasToolTrace);
            Assert.Contains("src/A.cs", messages[^1].ToolTraceText);
            Assert.True(messages[^1].HasDiagnostics);
            Assert.Contains("inconnues", messages[^1].DiagnosticsTitle);
            Assert.Contains("100 tokens entrants", messages[^1].DiagnosticsText);
            window.FindControl<Button>("ProjectToolsButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            PumpUntil(() => window.OwnedWindows.Count > 0);
            var toolsDialog = window.OwnedWindows.Single();
            Assert.Contains(toolsDialog.GetVisualDescendants().OfType<SelectableTextBlock>(), t => t.Text!.EndsWith($"/conversations/{conversation.Id}/mcp"));
            toolsDialog.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "Fermer")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            PumpUntil(() => window.FindControl<Button>("RefreshButton")!.IsEnabled);
            Assert.Equal("", window.FindControl<TextBox>("MessageInput")!.Text);
            Assert.Equal(2, window.FindControl<ItemsControl>("MessageList")!.ItemCount);

            files = [new(Guid.NewGuid(), "projet/notes.md", 1024, "Texte extrait", 3, DateTimeOffset.UtcNow, null, "hash")];
            Click(window, "RefreshButton");
            Assert.Equal(3, window.FindControl<ItemsControl>("MessageList")!.ItemCount);
            Assert.True(window.FindControl<Button>("UploadButton")!.IsEnabled);
            Assert.True(window.FindControl<Button>("FolderButton")!.IsEnabled);
            Assert.Contains(window.GetVisualDescendants().OfType<Button>(), button => Equals(button.Content, "Télécharger") && button.IsVisible);
            // Exercise the real deferred renderer, including controls recreated by a history refresh.
            PumpUntil(() => window.FindControl<ItemsControl>("MessageList")!.GetVisualDescendants()
                .OfType<MarkdownView>().Count(view => view.Children.Count > 0) == 3);
            Assert.Contains(window.FindControl<ItemsControl>("MessageList")!.GetVisualDescendants()
                .OfType<MarkdownView>(), view => view.IsMarkdown && view.Children.Count >= 5);

            var evidence = window.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "Sources et détails") && b.IsVisible);
            evidence.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            PumpUntil(() => window.FindControl<Border>("Inspector")!.IsVisible);
            var inspector = window.FindControl<StackPanel>("InspectorBody")!;
            inspector.Children.OfType<Button>().Single(b => b.Content?.ToString()?.Contains("src/A.cs") == true)
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            PumpUntil(() => inspector.Children.OfType<MarkdownView>().Any(v => v.Text.Contains("passage") && v.Text.Contains(citedHash)));
            var diagnostics = inspector.Children.OfType<Expander>().Single(e => Equals(e.Header, "Détails de la réponse"));
            diagnostics.IsExpanded = true;
            PumpUntil(() => diagnostics.GetVisualDescendants().OfType<SelectableTextBlock>().Any(t => t.Text?.Contains("S000000000000") == true));
            Click(window, "CloseInspector");
            Assert.False(window.FindControl<Border>("Inspector")!.IsVisible);
            // Optional visual evidence from the actual Avalonia/Skia rendering.
            if (Environment.GetEnvironmentVariable("ALYCIA_UI_CAPTURE") is { Length: > 0 } screenshot)
            {
                window.FindControl<ScrollViewer>("MessageScroll")!.ScrollToHome();
                Dispatcher.UIThread.RunJobs();
                using var frame = window.CaptureRenderedFrame();
                Assert.NotNull(frame);
                frame.Save(screenshot, global::Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
                var themeBefore = window.RequestedThemeVariant;
                var oldWidth = window.Width;
                var oldHeight = window.Height;
                foreach (var (variant, name, width, height, size) in new[] {
                    (global::Avalonia.Styling.ThemeVariant.Light, "aube-1280", 1280, 720, 18),
                    (global::Avalonia.Styling.ThemeVariant.Dark, "nuit-1920", 1920, 1080, 18),
                    (global::Avalonia.Styling.ThemeVariant.Light, "aube-200pc", 1280, 720, 36) })
                {
                    window.RequestedThemeVariant = variant;
                    window.Width = width; window.Height = height;
                    window.Resources["ReadingSize"] = (double)size;
                    Dispatcher.UIThread.RunJobs();
                    foreach (var view in window.GetVisualDescendants().OfType<MarkdownView>().ToArray()) view.RenderNow();
                    Dispatcher.UIThread.RunJobs();
                    using var themed = window.CaptureRenderedFrame();
                    Assert.NotNull(themed);
                    themed.Save(Path.ChangeExtension(screenshot, name + ".png"), global::Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
                }
                window.Resources.Remove("ReadingSize");
                window.RequestedThemeVariant = themeBefore;
                window.Width = oldWidth; window.Height = oldHeight;
            }

            if (Environment.GetEnvironmentVariable("ALYCIA_UI_BENCHMARK") is { Length: > 0 } benchmarkPath)
            {
                var original = messages.ToArray();
                var priorFiles = files;
                messages.Clear();
                files = [];
                messages.AddRange(Enumerable.Range(1, 1000).Select(i => new ChatMessageItem(conversation.Id, i, Guid.Empty,
                    i % 2 == 0 ? "assistant" : "user", i % 2 == 0 ? $"## Document {i}\\n\\nUn passage **français** et une mesure de lecture." : $"Analyser le document {i}.", DateTimeOffset.UnixEpoch)));
                var bytesBefore = GC.GetTotalAllocatedBytes();
                var watch = System.Diagnostics.Stopwatch.StartNew();
                Click(window, "RefreshButton");
                for (var page = 1; page < 10; page++) Click(window, "OlderButton");
                var list = window.FindControl<ItemsControl>("MessageList")!;
                PumpUntil(() => list.GetVisualDescendants().OfType<MarkdownView>().Count(view => view.Children.Count > 0) == 1000, 120);
                var historyMs = watch.Elapsed.TotalMilliseconds;
                var stream = window.FindControl<MarkdownView>("StreamingText")!;
                var large = string.Concat(Enumerable.Repeat("Un paragraphe **important** et du code en ligne.\\n\\n", 2200))[..100000];
                watch.Restart();
                for (int end = 25000; end <= large.Length; end += 25000) { stream.Text = large[..end]; stream.RenderNow(); }
                watch.Stop();
                File.WriteAllText(benchmarkPath, JsonSerializer.Serialize(new { messages = 1000, characters = 100000,
                    fragments = 4, historyMs, markdownMs = watch.Elapsed.TotalMilliseconds,
                    allocatedBytes = GC.GetTotalAllocatedBytes() - bytesBefore, workingSetBytes = Environment.WorkingSet }));
                stream.Text = "";
                messages.Clear();
                messages.AddRange(original);
                files = priorFiles;
                Click(window, "RefreshButton");
            }
            window.Width = 960; window.Height = 700;
            PumpUntil(() => !window.FindControl<Border>("Sidebar")!.IsVisible);
            Assert.True(window.FindControl<ScrollViewer>("MessageScroll")!.Bounds.Width > 800);
            if (Environment.GetEnvironmentVariable("ALYCIA_UI_CAPTURE") is { Length: > 0 } compact)
            {
                using var frame = window.CaptureRenderedFrame();
                Assert.NotNull(frame);
                frame.Save(Path.ChangeExtension(compact, "compact.png"), global::Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
            }

            window.FindControl<Button>("AnalysisButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            PumpUntil(() => window.OwnedWindows.Count > 0);
            var analysisDialog = window.OwnedWindows.Single();
            Assert.True(analysisDialog.Bounds.Height <= window.Bounds.Height - 32);
            Assert.True(analysisDialog.Bounds.Width <= window.Bounds.Width - 32);
            Assert.Contains(analysisDialog.GetVisualDescendants().OfType<ComboBox>(), box => box.SelectedItem is AnalysisRunItem { CompletedUnits: 4, ReusedUnits: 2 });
            var resumeButton = analysisDialog.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "Reprendre"));
            Assert.True(resumeButton.IsVisible && resumeButton.Focusable);
            if (Environment.GetEnvironmentVariable("ALYCIA_UI_CAPTURE") is { Length: > 0 } analysisImage)
            {
                using var frame = analysisDialog.CaptureRenderedFrame();
                Assert.NotNull(frame);
                frame.Save(Path.ChangeExtension(analysisImage, "analysis.png"), global::Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
            }
            analysisDialog.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
            analysisDialog.KeyRelease(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
            analysisDialog.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            // Escape closes and disposes the dialog synchronously; key-up belongs to the owner.
            window.KeyRelease(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            PumpUntil(() => window.OwnedWindows.Count == 0 && window.FindControl<Button>("RefreshButton")!.IsEnabled);

            fail = true;
            window.FindControl<TextBox>("MessageInput")!.Text = "Conserver ce brouillon";
            beforeFailure = () => window.FindControl<TextBox>("MessageInput")!.Text = "";
            Click(window, "SendButton");
            Assert.Equal("Conserver ce brouillon", window.FindControl<TextBox>("MessageInput")!.Text);
            var restored = new DraftStore(stateDirectory).Get(conversation.Id)!;
            Assert.Equal("Conserver ce brouillon", restored.Text);
            var retryIds = new List<Guid>();
            beforeFailure = () => retryIds.Add(new DraftStore(stateDirectory).Get(conversation.Id)!.RequestId);
            Click(window, "SendButton");
            Assert.Equal(restored.RequestId, Assert.Single(retryIds));
            Assert.Equal("Ollama indisponible", window.FindControl<TextBlock>("FeedbackText")!.Text);
            Assert.False(window.FindControl<StackPanel>("EmptyState")!.IsVisible);
            Assert.Equal("Vous · demande non confirmée", window.FindControl<TextBlock>("PendingUserLabel")!.Text);
            window.FindControl<Button>("DeleteButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            PumpUntil(() => window.OwnedWindows.Count > 0);
            var dialog = window.OwnedWindows.Single();
            var confirm = dialog.GetVisualDescendants().OfType<Button>()
                .Single(button => Equals(button.Content, "Supprimer définitivement"));
            confirm.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            PumpUntil(() => window.FindControl<Button>("RefreshButton")!.IsEnabled);
            Assert.Null(conversation);
            Assert.Equal(0, window.FindControl<ItemsControl>("MessageList")!.ItemCount);
            Assert.False(window.FindControl<Expander>("StreamingPanel")!.IsVisible);
            Assert.False(window.FindControl<Border>("PendingUserPanel")!.IsVisible);
            Assert.True(window.FindControl<StackPanel>("EmptyState")!.IsVisible);
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

    private static void PumpUntil(Func<bool> condition, int seconds = 5)
    {
        var timeout = DateTime.UtcNow.AddSeconds(seconds);
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
