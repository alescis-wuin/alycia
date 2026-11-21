using Alycia.Modules.Chat.Application.Context;
using System.Text.Json;
using Alycia.Modules.Chat.Application;
using Alycia.Modules.Chat.Domain;

namespace Alycia.Api.Chat;

internal static partial class ChatEndpoints
{
    private static void MapMemory(RouteGroupBuilder routes)
    {
        routes.MapPost("/conversations/{id:guid}/memory/reset", async (Guid id, MemoryRequest request,
            IConversationMemory memory, CancellationToken ct) => Results.Ok(await memory.ResetAsync(id, request.Revision, ct)));

        routes.MapGet("/conversations/{id:guid}/memory/temporal", async (Guid id, int? after, int? limit, IChatRepository repository,
            ITemporalMemoryIndex store, CancellationToken ct) =>
        {
            _ = await repository.FindAsync(id, ct) ?? throw new ConversationNotFoundException();
            if (after is < 0 || limit is < 1 or > 200) throw new ArgumentException("Page de mémoire invalide.");
            var items = await store.ExportAsync(id, after ?? 0, limit ?? 50, ct);
            return Results.Ok(new { version = 1, items, nextAfter = items.LastOrDefault()?.Sequence });
        });
        routes.MapDelete("/conversations/{id:guid}/memory/temporal", async (Guid id, IChatRepository repository, ITemporalMemoryIndex store, CancellationToken ct) =>
        {
            _ = await repository.FindAsync(id, ct) ?? throw new ConversationNotFoundException();
            await store.ClearAsync(id, ct);
            return Results.NoContent();
        });
        routes.MapPost("/conversations/{id:guid}/context/prepare", async (Guid id, MemoryRequest request,
            IChatRepository repository, IChatModel model, ITurnTools tools, ITurnContext turnContext, IConversationMemory memoryWriter, IProjectRetrieval retrieval, ITemporalMemory memory, ChatOptions defaults, HttpContext http, CancellationToken ct) =>
        {
            using var lease = GenerationGate.Shared.Enter(id);
            var conversation = await repository.FindAsync(id, ct) ?? throw new ConversationNotFoundException();
            ConversationConfiguration.CheckRevision(conversation, request.Revision);
            var text = Conversation.RequiredText(request.Content, 32000, "La demande");
            var options = await model.PrepareOptionsAsync(conversation.Model, ConversationConfiguration.Options(conversation, defaults), ct);
            var measured = new MeteredChatModel(model);
            await QueryRouting.PrepareAsync(repository, conversation, text, options, retrieval, measured, ct, memory);
            var useTools = await tools.AvailableForContextAsync(conversation, options, model, ct);
            var budget = await turnContext.AllocateAsync(conversation, text, options, useTools, ct);
            http.Response.ContentType = "application/x-ndjson; charset=utf-8";
            http.Response.Headers.CacheControl = "no-store";
            async Task WriteAsync(object item)
            {
                await http.Response.WriteAsync(JsonSerializer.Serialize(item, StreamJson) + "\n", ct);
                await http.Response.Body.FlushAsync(ct);
            }
            try
            {
                var result = await turnContext.BuildAsync(conversation, text, budget, model, measured, TurnContextMode.PrepareMemory,
                    status => WriteAsync(new { type = "delta", content = "", status }), ct);
                if (memoryWriter.Apply(conversation, result.Prepared)) await repository.SaveAsync(ct);
                await WriteAsync(new
                {
                    type = "done",
                    result = new
                    {
                        context = result.Window,
                        options,
                        usage = measured.Snapshot(options),
                        conversation.Revision,
                        omittedMessages = conversation.MessageCount - result.Window.HistoryMessagesIncluded,
                        estimator = TokenBudget.Description(options)
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
