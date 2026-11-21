using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Alycia.Client.Avalonia;
using Alycia.Contracts.Chat;

namespace Alycia.Client.Avalonia.Tests;

public sealed class InspectorWorkflowTests
{
    [Fact]
    public void CitationResolvesOnlyAgainstTheSelectedReplyAndItsRecordedSourceVersion()
    {
        var chat = Guid.NewGuid();
        var source = new SuppliedSource("S123456789ABC", Guid.NewGuid(), "doc.md", new string('a', 64), 1, 2, 4, 37, "initial", false);
        var report = new TurnDiagnostics(Citations: new("references_recognized", [source], [source.Reference], []));
        var chosen = new ChatMessageItem(chat, 2, Guid.NewGuid(), "assistant", "Réponse", DateTimeOffset.UnixEpoch,
            DiagnosticsJson: JsonSerializer.Serialize(report, ChatJson.Options));
        var later = chosen with { Sequence = 4, RequestId = Guid.NewGuid() };
        var foreign = chosen with { ConversationId = Guid.NewGuid() };
        var messages = new[] { chosen, later, foreign };

        var selection = new EvidenceSelection(chat, chosen, messages);
        Assert.Equal(source, selection.Resolve(chosen, source.Reference.ToLowerInvariant()));
        Assert.Null(selection.Resolve(later, source.Reference));
        Assert.Null(selection.Resolve(foreign, source.Reference));
        Assert.Null(selection.Resolve(chosen, "S000000000000"));
        Assert.Equal(later, new EvidenceSelection(chat, foreign, messages).Message);
        Assert.Null(new EvidenceSelection(chat, null, [foreign, chosen with { AttachmentId = Guid.NewGuid() }]).Message);
    }

    [Theory]
    [InlineData(false, true, 2, "Outils désactivés")]
    [InlineData(true, false, 2, "Ce modèle n’annonce pas la capacité tools")]
    [InlineData(true, true, 0, "Sans quota d’appels")]
    [InlineData(true, true, 2, "2 appels par demande")]
    public async Task ToolStatusCountsAndEndpointUseTheSameConversation(bool enabled, bool supported, int maxCalls, string expectedStatus)
    {
        var chat = Guid.NewGuid();
        var info = new ProjectToolsInfo(enabled, supported, maxCalls, ["project_read"], "2025-11-25");
        var files = new FileItem[]
        {
            new(Guid.NewGuid(), "main.cs", 12, "Texte extrait", 1, DateTimeOffset.UnixEpoch, null, "A"),
            new(Guid.NewGuid(), "notes.txt", 20, "Texte extrait", 1, DateTimeOffset.UnixEpoch, null, "B", "excluded")
        };
        using var http = new HttpClient(new Reply(request => request.RequestUri!.AbsolutePath switch
        {
            var path when path == $"/api/chat/conversations/{chat}/tools" => JsonContent.Create(info),
            var path when path == $"/api/chat/conversations/{chat}/files" => JsonContent.Create(files),
            var path => throw new InvalidOperationException(path)
        }));
        var client = new ChatApiClient(http, "http://127.0.0.1:5187", TimeSpan.FromSeconds(2));
        var state = await ToolInspection.LoadAsync(client, chat, default);

        Assert.Equal("2 fichiers importés · 1 fichiers de code · 1 exclusions", state.Files);
        Assert.Contains(expectedStatus, state.Availability);
        Assert.Equal(info.ProtocolVersion, state.ProtocolVersion);
        Assert.Equal($"http://127.0.0.1:5187/api/chat/conversations/{chat}/mcp", state.Endpoint);
    }

    private sealed class Reply(Func<HttpRequestMessage, HttpContent> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = respond(request) });
    }
}
