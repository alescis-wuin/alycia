using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Alycia.Contracts.Chat;

namespace Alycia.Client.Avalonia.Tests;

public sealed class ConfigurationTransferTests
{
    [Fact]
    public async Task ExportThenImportUsesTargetRevisionAndCopiesSettingsExactly()
    {
        var source = new ConversationSummary(Guid.NewGuid(), "Source", "demo", "Consignes sources",
            DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, 1, 0, "Notes sources");
        var target = Guid.NewGuid();
        var options = new ChatSettings
        {
            RelevantThreshold = .456789,
            Temperature = .123456,
            ToolMaxCalls = 57,
            Enrichment = new(true, "local-annotations", JobCalls: 234, TimeoutSeconds: 378)
        };
        var calls = 0;
        using var http = new HttpClient(new Handler(async (request, ct) =>
        {
            calls++;
            if (request.Method == HttpMethod.Get)
            {
                Assert.EndsWith($"/{source.Id}/settings", request.RequestUri!.AbsolutePath);
                return new(HttpStatusCode.OK) { Content = JsonContent.Create(new { conversation = source, options }) };
            }
            Assert.Equal(HttpMethod.Put, request.Method);
            Assert.EndsWith($"/{target}/settings", request.RequestUri!.AbsolutePath);
            using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
            var body = json.RootElement;
            Assert.Equal(42, body.GetProperty("revision").GetInt32());
            Assert.Equal(source.Model, body.GetProperty("model").GetString());
            Assert.Equal(source.SystemPrompt, body.GetProperty("systemPrompt").GetString());
            Assert.Equal(source.ContextNotes, body.GetProperty("contextNotes").GetString());
            var imported = body.GetProperty("options").Deserialize<ChatSettings>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
            Assert.Equal(JsonSerializer.Serialize(options), JsonSerializer.Serialize(imported));
            Assert.False(body.TryGetProperty("messages", out _));
            return new(HttpStatusCode.OK) { Content = JsonContent.Create(new { id = target, model = "demo", revision = 43 }) };
        }));
        var client = new ChatApiClient(http, "http://127.0.0.1:5187", TimeSpan.FromSeconds(5));
        var bytes = await client.ExportConfigurationAsync(source.Id, default);
        var result = await client.ImportConfigurationAsync(target, 42, bytes, default);
        Assert.Equal(target, result.Id); Assert.Equal(43, result.Revision); Assert.Equal(2, calls);
    }

    [Fact]
    public async Task InvalidFileNeverSendsAnUpdate()
    {
        using var http = new HttpClient(new Handler((_, _) => throw new InvalidOperationException("No request expected")));
        var client = new ChatApiClient(http, "http://127.0.0.1:5187", TimeSpan.FromSeconds(5));
        await Assert.ThrowsAsync<ArgumentException>(() => client.ImportConfigurationAsync(Guid.NewGuid(), 4, "{}"u8.ToArray(), default));
    }

    [Theory]
    [InlineData(HttpStatusCode.Conflict)]
    [InlineData(HttpStatusCode.BadRequest)]
    public async Task ImportDoesNotRetryWithAnotherRevisionOrSubstituteAnUnavailableModel(HttpStatusCode status)
    {
        var calls = 0;
        using var http = new HttpClient(new Handler((_, _) =>
        {
            calls++;
            return Task.FromResult(new HttpResponseMessage(status) { Content = JsonContent.Create(new { message = "Import refusé." }) });
        }));
        var client = new ChatApiClient(http, "http://127.0.0.1:5187", TimeSpan.FromSeconds(5));
        var bytes = SettingsFileCodec<ChatSettings>.Export("modèle-local", "", "", new());
        await Assert.ThrowsAsync<ChatApiException>(() => client.ImportConfigurationAsync(Guid.NewGuid(), 4, bytes, default));
        Assert.Equal(1, calls);
    }

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> action) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => action(request, ct); }
}
