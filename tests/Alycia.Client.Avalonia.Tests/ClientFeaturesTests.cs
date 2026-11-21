using System.Net;
using System.Text;
using System.Text.Json;
using Alycia.Client.Avalonia;
using Alycia.Contracts.Chat;

namespace Alycia.Client.Avalonia.Tests;

public sealed class ClientFeaturesTests
{
    [Fact]
    public void PersistedContextWarningAndToolFailureAreVisibleInMessageDetails()
    {
        var message = new ChatMessageItem(Guid.NewGuid(), 2, Guid.NewGuid(), "assistant", "Réponse", DateTimeOffset.UtcNow)
        {
            DiagnosticsJson = JsonSerializer.Serialize(new TurnDiagnostics(Warnings: ["Le résumé des anciens résultats d’outils est vide. L’exploration continue avec des extraits partiels ; certaines informations anciennes doivent être relues."]), ChatJson.Options),
            ToolTraceJson = JsonSerializer.Serialize(new[] { new ToolObservation(1, "project_read", "{}", false, 40,
                Error: "Identifiant de fichier inconnu.") }, ChatJson.Options)
        };
        Assert.Contains("partiellement conservé", message.DiagnosticsTitle);
        Assert.Contains("Le résumé des anciens résultats d’outils est vide. L’exploration continue avec des extraits partiels ; certaines informations anciennes doivent être relues.", message.DiagnosticsText);
        Assert.Contains("Motif : Identifiant de fichier inconnu.", message.ToolTraceText);
        Assert.Contains("refus ou erreur", message.ToolTraceText);
    }

    [Fact]
    public void DraftsRemainSeparateAndKeepRequestIdAcrossRestart()
    {
        var directory = Path.Combine(Path.GetTempPath(), "alycia-drafts-" + Guid.NewGuid().ToString("N"));
        try
        {
            var first = Guid.NewGuid(); var second = Guid.NewGuid();
            var store = new DraftStore(directory);
            var pending = store.Set(first, "question");
            store.Set(second, "another question"); store.Save();
            var reopened = new DraftStore(directory);
            Assert.Equal(pending, reopened.Get(first));
            Assert.Equal(pending.RequestId, reopened.Set(first, " question ").RequestId);
            Assert.NotEqual(pending.RequestId, reopened.Set(first, "changed").RequestId);
            reopened.Remove(first); reopened.Save();
            Assert.Null(new DraftStore(directory).Get(first));
            Assert.Equal("another question", new DraftStore(directory).Get(second)!.Text);
            File.WriteAllText(Path.Combine(directory, "drafts.json"), "corrupt");
            var damaged = new DraftStore(directory); damaged.Set(first, "new"); damaged.Save();
            Assert.NotNull(damaged.Error);
            Assert.Equal("corrupt", File.ReadAllText(Path.Combine(directory, "drafts.json")));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public void CodeCopyHandlesLongFencesAndPreservesIndentation()
    {
        Assert.Equal("    return 42;\n\nprint('🙂')", CodeBlocks.Extract("intro\n````cs\n    return 42;\n````\ntext\n~~~py\nprint('🙂')\n~~~\n"));
        Assert.Equal("", CodeBlocks.Extract("No fenced code"));
    }

    [Theory]
    [InlineData("project/.git/config")]
    [InlineData("src/node_modules/lib.js")]
    [InlineData(".env.production")]
    [InlineData("server.key")]
    public void ExcludesGeneratedAndKnownSensitivePaths(string path) => Assert.True(ImportPolicy.Excluded(path));

    [Fact]
    public async Task ClientRequiresDoneAfterDeltasAndPreservesRevision()
    {
        var id = Guid.NewGuid(); var requestId = Guid.NewGuid();
        using var http = new HttpClient(new Handler(async (request, ct) =>
        {
            Assert.EndsWith($"/{id}/stream", request.RequestUri!.AbsolutePath);
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
            Assert.Equal(7, body.RootElement.GetProperty("revision").GetInt32());
            Assert.Equal(requestId, body.RootElement.GetProperty("requestId").GetGuid());
            return new(HttpStatusCode.OK) { Content = new StringContent("{\"type\":\"delta\",\"content\":\"partial\"}\n", Encoding.UTF8, "application/x-ndjson") };
        }));
        var received = new List<ModelDelta>();
        var client = new ChatApiClient(http, "http://127.0.0.1:5187", TimeSpan.FromSeconds(2));
        await Assert.ThrowsAsync<ChatApiException>(() => client.StreamMessageAsync(id, requestId, "question", 7,
            delta => { received.Add(delta); return Task.CompletedTask; }, default));
        Assert.Equal("partial", Assert.Single(received).Content);
    }

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> action) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => action(request, ct); }
}
