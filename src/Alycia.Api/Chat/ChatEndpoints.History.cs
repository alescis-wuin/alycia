using System.Text;
using Alycia.Modules.Chat.Application;
using Alycia.Modules.Chat.Infrastructure;

namespace Alycia.Api.Chat;

internal static partial class ChatEndpoints
{
    private static void MapHistory(RouteGroupBuilder routes)
    {
        routes.MapGet("/search", async (string? q, int? offset, Guid? workspaceId, bool? unassigned, ConversationHistory history, CancellationToken ct) =>
        {
            var page = await history.SearchAsync(q, offset, workspaceId, unassigned, ct);
            return Results.Ok(new { items = page.Items.Select(c => c.ToResponse()), hasMore = page.HasMore });
        });

        routes.MapGet("/conversations/{id:guid}/export", async (Guid id, string? format, ConversationHistory history, HttpContext http, CancellationToken ct) =>
        {
            var archive = await history.ArchiveAsync(id, ct);
            // Same portable size bound for either representation.
            var json = ConversationTransfer.ExportJson(archive);
            http.Response.Headers.CacheControl = "no-store";
            return format switch
            {
                null or "json" => Results.File(json, "application/json", $"alycia-{id:N}.json"),
                "markdown" => Results.File(Encoding.UTF8.GetBytes(ConversationTransfer.Markdown(archive)),
                    "text/markdown; charset=utf-8", $"alycia-{id:N}.md"),
                _ => throw new ArgumentException("Format d’export attendu : json ou markdown.")
            };
        });

        routes.MapPost("/import", async (HttpRequest request, ConversationHistory history, CancellationToken ct) =>
        {
            if (request.ContentLength > ConversationTransfer.MaxBytes) throw new ArgumentException("Import limité à 5 Mio.");
            var bytes = await FileImport.ReadBoundedAsync(request.Body, ConversationTransfer.MaxBytes, ct);
            var conversation = await history.ImportAsync(bytes, ct);
            return Results.Created($"/api/chat/conversations/{conversation.Id}", conversation.ToResponse());
        });

        routes.MapPost("/conversations/{id:guid}/fork", async (Guid id, ForkConversation request, ConversationHistory history, CancellationToken ct) =>
        {
            var conversation = await history.ForkAsync(id, request.Revision, request.ThroughSequence, ct);
            return Results.Created($"/api/chat/conversations/{conversation.Id}", conversation.ToResponse());
        });
    }

    private sealed record ForkConversation(int Revision, int? ThroughSequence);
}
