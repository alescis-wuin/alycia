using System.Text.Json;
using Alycia.Modules.Chat.Application;
using Alycia.Modules.Chat.Domain;
using Alycia.Modules.Chat.Infrastructure;

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
            conversation.SetMemoryLedger("{}");
            await repository.SaveAsync(ct);
            return Results.Ok(conversation);
        });

        routes.MapGet("/conversations/{id:guid}/memory/temporal", async (Guid id, int? after, int? limit, IChatRepository repository,
            TemporalMemoryStore store, CancellationToken ct) =>
        {
            _ = await repository.FindAsync(id, ct) ?? throw new ConversationNotFoundException();
            if (after is < 0 || limit is < 1 or > 200) throw new ArgumentException("Page de mémoire invalide.");
            var items = await store.ExportAsync(id, after ?? 0, limit ?? 50, ct);
            return Results.Ok(new { version = 1, items, nextAfter = items.LastOrDefault()?.Sequence });
        });
        routes.MapDelete("/conversations/{id:guid}/memory/temporal", async (Guid id, IChatRepository repository, TemporalMemoryStore store, CancellationToken ct) =>
        {
            _ = await repository.FindAsync(id, ct) ?? throw new ConversationNotFoundException();
            await store.ClearAsync(id, ct);
            return Results.NoContent();
        });
        routes.MapPost("/conversations/{id:guid}/context/prepare", async (Guid id, MemoryRequest request,
            IChatRepository repository, IChatModel model, IProjectTools tools, IProjectRetrieval retrieval, ITemporalMemory memory, ChatOptions defaults, HttpContext http, CancellationToken ct) =>
        {
            using var lease = GenerationGate.Shared.Enter(id);
            var conversation = await repository.FindAsync(id, ct) ?? throw new ConversationNotFoundException();
            ConversationConfiguration.CheckRevision(conversation, request.Revision);
            var text = Conversation.RequiredText(request.Content, 32000, "La demande");
            var options = await model.PrepareOptionsAsync(conversation.Model, ConversationConfiguration.Options(conversation, defaults), ct);
            var measured = new MeteredChatModel(model);
            await QueryRouting.PrepareAsync(repository, conversation, text, options, retrieval, measured, ct, memory);
            var useTools = options.UseProjectTools && await tools.HasSourcesAsync(id, ct) && await model.SupportsToolsAsync(conversation.Model, ct);
            var contextOptions = await ProjectAgent.ContextOptionsAsync(repository, conversation, text, options, useTools, ct);
            http.Response.ContentType = "application/x-ndjson; charset=utf-8";
            http.Response.Headers.CacheControl = "no-store";
            async Task WriteAsync(object item)
            {
                await http.Response.WriteAsync(JsonSerializer.Serialize(item, StreamJson) + "\n", ct);
                await http.Response.Body.FlushAsync(ct);
            }
            try
            {
                var result = await ConversationContext.BuildAsync(repository, measured, conversation, text, contextOptions, true,
                    status => WriteAsync(new { type = "delta", content = "", status }), ct);
                var assembled = await ContextGuides.AddAsync(result.Context, contextOptions, measured, true, ct);
                if (result.Changed)
                {
                    if (result.LedgerJson is not null) conversation.SetMemoryLedger(result.LedgerJson);
                    else conversation.SetMemory(result.Memory, result.Through, result.SourceHash, conversation.Model);
                    await repository.SaveAsync(ct);
                }
                await WriteAsync(new
                {
                    type = "done",
                    result = new
                    {
                        context = assembled with { Route = options.Route, ToolReserve = contextOptions.SafetyTokens - options.SafetyTokens },
                        options,
                        usage = measured.Snapshot(options),
                        conversation.Revision,
                        omittedMessages = conversation.MessageCount - result.Context.HistoryMessagesIncluded,
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
