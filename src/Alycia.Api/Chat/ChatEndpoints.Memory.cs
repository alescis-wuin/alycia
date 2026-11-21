using Alycia.Modules.Chat.Application.Conversations;
using Alycia.Modules.Chat.Application;
using Alycia.Modules.Chat.Application.Context;

namespace Alycia.Api.Chat;

internal static partial class ChatEndpoints
{
    private static void MapMemory(RouteGroupBuilder routes)
    {
        routes.MapPost("/conversations/{id:guid}/memory/reset", async (Guid id, MemoryRequest request,
            IConversationMemory memory, CancellationToken ct) => Results.Ok((await memory.ResetAsync(id, request.Revision, ct)).ToResponse()));

        routes.MapGet("/conversations/{id:guid}/memory/temporal", async (Guid id, int? after, int? limit, ConversationTemporalMemory memory, CancellationToken ct) =>
        {
            var items = await memory.ListAsync(id, after, limit, ct);
            return Results.Ok(new { version = 1, items, nextAfter = items.LastOrDefault()?.Sequence });
        });
        routes.MapDelete("/conversations/{id:guid}/memory/temporal", async (Guid id, ConversationTemporalMemory memory, CancellationToken ct) =>
        {
            await memory.ClearAsync(id, ct);
            return Results.NoContent();
        });
        routes.MapPost("/conversations/{id:guid}/context/prepare", async (Guid id, MemoryRequest request,
            ConversationContextPreparation preparation, HttpContext http, CancellationToken ct) =>
        {
            using var pending = await preparation.BeginAsync(id, request.Revision, request.Content, ct);
            http.Response.ContentType = "application/x-ndjson; charset=utf-8";
            http.Response.Headers.CacheControl = "no-store";
            Task WriteAsync(object item) => WriteEventAsync(http.Response, item, ct);
            try
            {
                var result = await pending.BuildAsync(status => WriteAsync(new { type = "delta", content = "", status }));
                await WriteAsync(new { type = "done", result });
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
            catch (Exception exception) when (http.Response.HasStarted && !ct.IsCancellationRequested)
            {
                await WriteAsync(new
                {
                    type = "error",
                    message = exception is ArgumentException or ChatModelException or ChatConflictException
                    ? exception.Message : "La préparation n’a pas été confirmée. Rechargez la conversation."
                });
            }
            return Results.Empty;
        });
    }

    private sealed record MemoryRequest(int Revision, string? Content);
}
