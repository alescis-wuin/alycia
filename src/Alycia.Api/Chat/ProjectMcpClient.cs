using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Alycia.Modules.Chat.Application;
using Alycia.Modules.Chat.Infrastructure;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;

namespace Alycia.Api.Chat;

// Destination derived from Kestrel, never from the model or a request Host header.
public sealed class ProjectMcpClient(HttpClient http, IServer server, IConfiguration configuration, ProjectToolService sources) : IProjectTools
{
    private Guid? _initialized;
    private int _sequence;
    public Task<bool> HasSourcesAsync(Guid conversation, CancellationToken ct) => sources.HasSourcesAsync(conversation, ct);
    public async Task<ProjectToolResult> CallAsync(Guid conversation, string name, JsonElement arguments, int maxResultBytes, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(60));
        try
        {
            if (_initialized != conversation)
            {
                var init = await RpcAsync(conversation, "initialize", new
                { protocolVersion = "2025-11-25", capabilities = new { }, clientInfo = new { name = "alycia-ollama", version = "0.3.0" } }, timeout.Token);
                if (init.GetProperty("protocolVersion").GetString() != "2025-11-25") throw new ChatModelException("Version MCP locale inattendue.");
                await RpcAsync(conversation, "notifications/initialized", new { }, timeout.Token, true);
                var catalog = await RpcAsync(conversation, "tools/list", new { }, timeout.Token);
                var names = catalog.GetProperty("tools").EnumerateArray().Select(t => t.GetProperty("name").GetString()).ToHashSet();
                if (ProjectToolCatalog.All.Any(t => !names.Contains(t.Name))) throw new ChatModelException("Catalogue MCP local incomplet.");
                _initialized = conversation;
            }
            var result = await RpcAsync(conversation, "tools/call", new
            { name, arguments, _meta = new Dictionary<string, int> { ["io.alycia/maxResultBytes"] = maxResultBytes } }, timeout.Token);
            var text = string.Join("\n", result.GetProperty("content").EnumerateArray().Where(c => c.GetProperty("type").GetString() == "text")
                .Select(c => c.GetProperty("text").GetString()));
            ToolEvidence? evidence = null;
            IReadOnlyList<ToolEvidence>? evidenceItems = null;
            IReadOnlyList<HistoryEvidence>? history = null;
            if (result.TryGetProperty("_meta", out var meta) && meta.TryGetProperty("io.alycia/evidence", out var source) && source.ValueKind != JsonValueKind.Null)
                evidence = source.Deserialize<ToolEvidence>(ProjectToolCatalog.Json);
            if (result.TryGetProperty("_meta", out var batchMeta) && batchMeta.TryGetProperty("io.alycia/evidenceItems", out var batch) && batch.ValueKind != JsonValueKind.Null)
                evidenceItems = batch.Deserialize<ToolEvidence[]>(ProjectToolCatalog.Json);
            if (result.TryGetProperty("_meta", out var historyMeta) && historyMeta.TryGetProperty("io.alycia/history", out var historyJson) && historyJson.ValueKind != JsonValueKind.Null)
                history = historyJson.Deserialize<HistoryEvidence[]>(ProjectToolCatalog.Json);
            return new(text, result.TryGetProperty("isError", out var error) && error.GetBoolean(), evidence, evidenceItems, history);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new ChatModelException("Le MCP local n’a pas répondu dans le délai prévu."); }
        catch (HttpRequestException) { throw new ChatModelException("Le MCP local est inaccessible. Vérifiez la configuration du backend."); }
        catch (Exception e) when (e is JsonException or InvalidOperationException or KeyNotFoundException or ArgumentException)
        { throw new ChatModelException("Réponse MCP locale invalide."); }
    }
    private async Task<JsonElement> RpcAsync(Guid conversation, string method, object parameters, CancellationToken ct, bool notification = false)
    {
        var address = server.Features.Get<IServerAddressesFeature>()?.Addresses
            .OrderBy(a => a.StartsWith("https:", StringComparison.OrdinalIgnoreCase)).FirstOrDefault()
            ?? throw new ChatModelException("Adresse d’écoute MCP indisponible.");
        var origin = new UriBuilder(address.Replace("://*:", "://127.0.0.1:").Replace("://+:", "://127.0.0.1:"));
        if (origin.Host is "::" or "[::]") origin.Host = "[::1]";
        else if (!origin.Uri.IsLoopback) origin.Host = "127.0.0.1";
        origin.Path = $"/api/chat/conversations/{conversation}/mcp";
        var id = ++_sequence;
        using var request = new HttpRequestMessage(HttpMethod.Post, origin.Uri);
        request.Headers.Accept.ParseAdd("application/json, text/event-stream");
        request.Headers.Add("MCP-Protocol-Version", "2025-11-25");
        if (configuration.GetValue<bool>("ALYCIA_CHAT_CONTAINER_MODE")) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", configuration["ALYCIA_CHAT_ACCESS_TOKEN"]);
        request.Content = JsonContent.Create(notification ? (object)new { jsonrpc = "2.0", method, @params = parameters }
            : new { jsonrpc = "2.0", id, method, @params = parameters });
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode) throw new ChatModelException($"Le MCP local a refusé la requête (HTTP {(int)response.StatusCode}).");
        if (notification) return default;
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var document = JsonDocument.Parse(await FileImport.ReadBoundedAsync(stream, 256 * 1024, ct));
        var root = document.RootElement;
        if (root.GetProperty("jsonrpc").GetString() != "2.0" || root.GetProperty("id").GetInt32() != id) throw new ChatModelException("Identifiant MCP invalide.");
        if (root.TryGetProperty("error", out _)) throw new ChatModelException("Le MCP local a signalé une erreur de protocole.");
        return root.GetProperty("result").Clone();
    }
}
