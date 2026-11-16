using System.Text.Json;
using Alycia.Modules.Chat.Application;
using Alycia.Modules.Chat.Domain;

namespace Alycia.Api.Chat;

internal static partial class ChatEndpoints
{
    private static void MapMemory(RouteGroupBuilder routes)
    {
        routes.MapPost("/conversations/{id:guid}/memory/reset", async (Guid id, MemoryRequest request,
            IChatRepository repository, CancellationToken ct) =>
        {
            using var lease = GenerationGate.Shared.Enter(id);
            var conversation = await repository.FindAsync(id, ct) ?? throw new ConversationNotFoundException();
            ConversationConfiguration.CheckRevision(conversation, request.Revision);
            conversation.SetMemory("", 0, "", "");
            await repository.SaveAsync(ct);
            return Results.Ok(conversation);
        });

        routes.MapPost("/conversations/{id:guid}/context/prepare", async (Guid id, MemoryRequest request,
            IChatRepository repository, IChatModel model, ChatOptions defaults, HttpContext http, CancellationToken ct) =>
        {
            using var lease = GenerationGate.Shared.Enter(id);
            var conversation = await repository.FindAsync(id, ct) ?? throw new ConversationNotFoundException();
            ConversationConfiguration.CheckRevision(conversation, request.Revision);
            var text = Conversation.RequiredText(request.Content, 32000, "La demande");
            var options = ConversationConfiguration.Options(conversation, defaults);
            http.Response.ContentType = "application/x-ndjson; charset=utf-8";
            http.Response.Headers.CacheControl = "no-store";
            async Task WriteAsync(object item)
            {
                await http.Response.WriteAsync(JsonSerializer.Serialize(item, StreamJson) + "\n", ct);
                await http.Response.Body.FlushAsync(ct);
            }
            try
            {
                var result = await RecursiveContext.BuildAsync(repository, model, conversation, text, options, true,
                    status => WriteAsync(new { type = "delta", content = "", status }), ct);
                if (result.Changed)
                {
                    conversation.SetMemory(result.Memory, result.Through, result.SourceHash, conversation.Model);
                    await repository.SaveAsync(ct);
                }
                await WriteAsync(new
                {
                    type = "done",
                    result = new
                    {
                        result.Context,
                        options,
                        conversation.Revision,
                        omittedMessages = conversation.MessageCount - result.Context.HistoryMessagesIncluded,
                        estimator = "UTF-8 conservateur ; mémoire récursive + deux derniers échanges protégés"
                    }
                });
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
