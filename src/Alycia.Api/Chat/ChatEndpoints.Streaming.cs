using System.Text.Json;
using Alycia.Modules.Chat.Application;

namespace Alycia.Api.Chat;

internal static partial class ChatEndpoints
{
    private static readonly JsonSerializerOptions StreamJson = new(JsonSerializerDefaults.Web);

    private static async Task WriteEventAsync(HttpResponse response, object item, CancellationToken ct)
    {
        await response.WriteAsync(JsonSerializer.Serialize(item, StreamJson) + "\n", ct);
        await response.Body.FlushAsync(ct);
    }

    private static void MapStreaming(RouteGroupBuilder routes)
    {
        routes.MapPost("/conversations/{id:guid}/stream", async (Guid id, StreamMessage request,
            ChatService service, HttpContext http, ILogger<ChatService> logger, CancellationToken ct) =>
        {
            http.Response.ContentType = "application/x-ndjson; charset=utf-8";
            http.Response.Headers.CacheControl = "no-store";
            http.Response.Headers["X-Accel-Buffering"] = "no";
            Task WriteAsync(object item) => WriteEventAsync(http.Response, item, ct);
            try
            {
                var result = await service.StreamAsync(id, request.RequestId, request.Content, request.Revision,
                    delta => WriteAsync(new { type = "delta", delta.Content, delta.Thinking, delta.Status, delta.Tool }), ct);
                // Only this final event acknowledges the atomic database commit.
                await WriteAsync(new { type = "done", result = result.ToResponse() });
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
            catch (Exception exception) when (http.Response.HasStarted && !ct.IsCancellationRequested)
            {
                // Do not log source/prompt text carried by exception messages.
                logger.LogWarning("Flux interrompu : {ExceptionType} ; {StackTrace}", Alycia.Contracts.Diagnostics.FailureType.Name(exception), exception.StackTrace);
                var message = exception switch
                {
                    ChatModelException or ChatConflictException or ArgumentException => exception.Message,
                    _ => "L’enregistrement n’a pas été confirmé. Actualisez l’historique avant de réessayer."
                };
                await WriteAsync(new { type = "error", message });
            }
            return Results.Empty;
        });
    }

    private sealed record StreamMessage(Guid RequestId, string? Content, int? Revision);
}
