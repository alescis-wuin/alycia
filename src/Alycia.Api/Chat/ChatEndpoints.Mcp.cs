using System.Text.Json;
using Alycia.Modules.Chat.Application;
using Alycia.Modules.Chat.Infrastructure;

namespace Alycia.Api.Chat;

internal static partial class ChatEndpoints
{
    private static readonly string[] McpVersions = ["2025-03-26", "2025-06-18", "2025-11-25"];
    private static void MapMcp(RouteGroupBuilder routes)
    {
        routes.MapGet("/conversations/{id:guid}/tools", async (Guid id, IChatRepository repository, IChatModel model, ChatOptions defaults, CancellationToken ct) =>
        {
            var conversation = await repository.FindAsync(id, ct) ?? throw new ConversationNotFoundException();
            var settings = ConversationConfiguration.Options(conversation, defaults);
            return Results.Ok(new
            {
                enabled = settings.UseProjectTools,
                modelSupportsTools = await model.SupportsToolsAsync(conversation.Model, ct),
                maxCalls = settings.ToolMaxCalls,
                tools = ProjectToolCatalog.All.Select(t => t.Name),
                protocolVersion = McpVersions[^1]
            });
        });
        const string path = "/conversations/{id:guid}/mcp";
        routes.MapMethods(path, ["GET", "DELETE"], (HttpContext http) =>
        { http.Response.Headers.Allow = "POST"; return Results.StatusCode(405); });
        routes.MapPost(path, async (Guid id, HttpContext http, IChatRepository repository, ProjectToolService tools, CancellationToken ct) =>
        {
            http.Response.Headers.CacheControl = "no-store";
            JsonElement? requestId = null;
            IResult Error(int code, string message, int status = 200) => Results.Json(new
            { jsonrpc = "2.0", id = requestId, error = new { code, message } }, statusCode: status);
            IResult Result(object value) => Results.Json(new { jsonrpc = "2.0", id = requestId, result = value });
            try
            {
                var version = http.Request.Headers["MCP-Protocol-Version"].ToString();
                if (version.Length > 0 && !McpVersions.Contains(version, StringComparer.Ordinal)) return Error(-32600, "Version MCP non prise en charge ; utilisez 2025-11-25.", 400);
                if (!http.Request.HasJsonContentType()) return Error(-32600, "Content-Type application/json requis.", 415);
                var accept = http.Request.Headers.Accept.ToString();
                if (!accept.Contains("application/json", StringComparison.OrdinalIgnoreCase) || !accept.Contains("text/event-stream", StringComparison.OrdinalIgnoreCase))
                    return Error(-32600, "Accept application/json, text/event-stream requis.", 406);
                var bytes = await FileImport.ReadBoundedAsync(http.Request.Body, 65536, ct);
                using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 16 });
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("jsonrpc", out var rpc)
                    || rpc.ValueKind != JsonValueKind.String || rpc.GetString() != "2.0"
                    || !root.TryGetProperty("method", out var method) || method.ValueKind != JsonValueKind.String)
                    return Error(-32600, "Requête JSON-RPC 2.0 invalide.", 400);
                if (root.TryGetProperty("id", out var key))
                {
                    if (!(key.ValueKind == JsonValueKind.String && key.GetString()!.Length <= 128)
                        && !(key.ValueKind == JsonValueKind.Number && key.TryGetInt64(out _))) return Error(-32600, "Identifiant JSON-RPC invalide.", 400);
                    requestId = key.Clone();
                }
                if (await repository.FindAsync(id, ct) is null) return Error(-32602, "Conversation introuvable.", 404);
                // Accepted notifications have no response and never execute an action.
                if (requestId is null) return Results.StatusCode(202);
                var args = root.TryGetProperty("params", out var parameters) ? parameters : JsonSerializer.SerializeToElement(new { });
                if (args.ValueKind != JsonValueKind.Object) return Error(-32602, "params doit être un objet.");
                switch (method.GetString())
                {
                    case "initialize":
                        if (!args.TryGetProperty("protocolVersion", out var protocol) || protocol.ValueKind != JsonValueKind.String)
                            return Error(-32602, "protocolVersion requis.");
                        return Result(new
                        {
                            protocolVersion = McpVersions.Contains(protocol.GetString()) ? protocol.GetString() : McpVersions[^1],
                            capabilities = new { tools = new { listChanged = false } },
                            serverInfo = new { name = "alycia-project", version = "0.3.0" },
                            instructions = "Sources importées de cette conversation en lecture seule ; exclusions et pagination explicites."
                        });
                    case "ping": return Result(new { });
                    case "tools/list":
                        if (args.TryGetProperty("cursor", out var cursor) && cursor.ValueKind != JsonValueKind.Null) return Error(-32602, "Catalogue sur une page, sans curseur.");
                        return Result(new
                        {
                            tools = ProjectToolCatalog.All.Select(t => new
                            {
                                name = t.Name,
                                description = t.Description,
                                inputSchema = t.InputSchema,
                                annotations = new { readOnlyHint = true, destructiveHint = false, idempotentHint = true, openWorldHint = false }
                            })
                        });
                    case "tools/call":
                        if (!args.TryGetProperty("name", out var name) || name.ValueKind != JsonValueKind.String
                            || !ProjectToolCatalog.All.Any(t => t.Name == name.GetString())) return Error(-32602, "Outil inconnu.");
                        var input = args.TryGetProperty("arguments", out var arguments) ? arguments : JsonSerializer.SerializeToElement(new { });
                        var budget = 24000;
                        if (args.TryGetProperty("_meta", out var meta) && meta.ValueKind == JsonValueKind.Object
                            && meta.TryGetProperty("io.alycia/maxResultBytes", out var size))
                        {
                            if (size.ValueKind != JsonValueKind.Number || !size.TryGetInt32(out budget) || budget is < 1024 or > 131072) return Error(-32602, "Budget invalide.");
                        }
                        var result = await tools.CallAsync(id, name.GetString()!, input, budget, ct);
                        return Result(new
                        {
                            content = new[] { new { type = "text", text = result.Text } },
                            isError = result.IsError,
                            _meta = new Dictionary<string, object?> { ["io.alycia/evidence"] = result.Evidence, ["io.alycia/evidenceItems"] = result.EvidenceItems, ["io.alycia/history"] = result.History }
                        });
                    default: return Error(-32601, "Méthode non prise en charge.");
                }
            }
            catch (JsonException) { return Error(-32700, "JSON invalide.", 400); }
            catch (ArgumentException) { return Error(-32602, "Paramètres invalides ou requête supérieure à 64 Kio.", 400); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { return Results.StatusCode(499); }
            catch { return Error(-32603, "Service MCP indisponible. Vérifiez le stockage local.", 500); }
        });
    }
}
