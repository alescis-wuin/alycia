using Alycia.Modules.Chat.Application.Context;
using Alycia.Modules.Chat.Application;
using Alycia.Modules.Chat.Domain;
using Alycia.Modules.Chat.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Alycia.Api.Chat;

internal static partial class ChatEndpoints
{
    private static void MapConfiguration(RouteGroupBuilder routes)
    {
        routes.MapGet("/import-settings", async (ImportSettingsStore settings, CancellationToken ct) => Results.Ok(await settings.LoadAsync(ct)));
        routes.MapPut("/import-settings", async (ImportLimits limits, ImportSettingsStore settings, CancellationToken ct) =>
        {
            await settings.SaveAsync(limits, ct);
            return Results.Ok(limits);
        });
        routes.MapGet("/conversations/{id:guid}/settings", async (Guid id, IChatRepository repository,
            ChatOptions defaults, CancellationToken ct) =>
        {
            var conversation = await repository.FindAsync(id, ct) ?? throw new ConversationNotFoundException();
            return Results.Ok(new { conversation, options = ConversationConfiguration.Options(conversation, defaults) });
        });

        routes.MapPut("/conversations/{id:guid}/settings", async (Guid id, ConfigureConversation request,
            IChatRepository repository, IChatModel model, CancellationToken ct) =>
        {
            var conversation = await repository.FindAsync(id, ct) ?? throw new ConversationNotFoundException();
            ConversationConfiguration.CheckRevision(conversation, request.Revision);
            if (request.Options is null) throw new ArgumentException("Réglages requis.");
            if (request.Model != conversation.Model && !(await model.ListModelsAsync(ct)).Contains(request.Model))
                throw new ArgumentException("Choisissez un modèle local capable de générer du texte.");
            conversation.Configure(request.Model, request.SystemPrompt, request.ContextNotes,
                ConversationConfiguration.Serialize(request.Options));
            SlidingContext.Build(ConversationConfiguration.System(conversation), [], "Bonjour", request.Options);
            await repository.SaveAsync(ct);
            return Results.Ok(conversation);
        });

        routes.MapPost("/conversations/{id:guid}/context", async (Guid id, PreviewContext request,
            IChatRepository repository, IChatModel model, ITurnTools tools, ITurnContext turnContext, IProjectRetrieval retrieval, ITemporalMemory memory, ChatOptions defaults, CancellationToken ct) =>
        {
            var conversation = await repository.FindAsync(id, ct) ?? throw new ConversationNotFoundException();
            var options = await model.PrepareOptionsAsync(conversation.Model, ConversationConfiguration.Options(conversation, defaults), ct);
            var text = Conversation.RequiredText(request.Content, 32000, "Le message");
            var measurements = new MeteredChatModel(model);
            await QueryRouting.PrepareAsync(repository, conversation, text, options, retrieval, measurements, ct, memory);
            var useTools = await tools.AvailableForContextAsync(conversation, options, model, ct);
            var budget = await turnContext.AllocateAsync(conversation, text, options, useTools, ct);
            var preview = await turnContext.BuildAsync(conversation, text, budget, model, measurements, TurnContextMode.Preview, null, ct);
            var context = preview.Window;
            return Results.Ok(new
            {
                context,
                options,
                conversation.Revision,
                omittedMessages = conversation.MessageCount - context.HistoryMessagesIncluded,
                usage = measurements.Snapshot(options),
                estimator = TokenBudget.Description(options)
            });
        });

        routes.MapGet("/conversations/{id:guid}/files", async (Guid id, ChatDbContext database, CancellationToken ct) =>
        {
            if (!await database.Conversations.AnyAsync(c => c.Id == id, ct)) throw new ConversationNotFoundException();
            // Project metadata in SQL: do not load extracted documents for this list.
            return Results.Ok(await database.ForConversation(id)
                .OrderBy(f => f.CreatedAtUtc).Select(f => new FileSummary(f.Id, f.Name, f.Size, f.Status,
                    f.AnchorSequence, f.CreatedAtUtc, f.ArchiveId, f.Sha256, f.ContextMode, f.BatchId, f.SourceCreatedAt, f.SourceModifiedAt, f.MetadataSource, f.WorkspaceId)).ToListAsync(ct));
        });

        routes.MapPatch("/conversations/{id:guid}/files/{fileId:guid}", async (Guid id, Guid fileId,
            ConfigureFile request, ChatDbContext database, IChatRepository repository, CancellationToken ct) =>
        {
            if (request.Mode is not ("window" or "pinned" or "excluded" or "project"))
                throw new ArgumentException("Mode documentaire invalide.");
            var conversation = await repository.FindAsync(id, ct) ?? throw new ConversationNotFoundException();
            ConversationConfiguration.CheckRevision(conversation, request.Revision);
            if (conversation.WorkspaceId is Guid project && await database.Attachments.AnyAsync(f => f.Id == fileId && f.WorkspaceId == project, ct))
            {
                if (request.Mode == "window") throw new ArgumentException("Une source du projet est disponible, épinglée ou exclue.");
                var entry = await database.WorkspaceFileOverrides.FindAsync([id, fileId], ct);
                if (entry is null) database.WorkspaceFileOverrides.Add(new() { ConversationId = id, FileId = fileId, Mode = request.Mode });
                else entry.Mode = request.Mode;
                conversation.AttachFiles();
                await repository.SaveAsync(ct);
                return Results.Ok(FileImport.Summary(await database.ForConversation(id).SingleAsync(f => f.Id == fileId, ct)));
            }
            var file = await database.Attachments.SingleOrDefaultAsync(f => f.ConversationId == id && f.Id == fileId, ct)
                ?? throw new ArgumentException("Fichier introuvable.");
            file.ContextMode = request.Mode;
            conversation.AttachFiles();
            await repository.SaveAsync(ct);
            return Results.Ok(FileImport.Summary(file));
        });

        routes.MapGet("/prompts", async (ChatDbContext database, CancellationToken ct) =>
            Results.Ok(await database.PromptProfiles.AsNoTracking().OrderBy(p => p.Name).ToListAsync(ct)));
        routes.MapPost("/prompts", async (SavePrompt request, ChatDbContext database, CancellationToken ct) =>
        {
            var profile = new PromptProfile
            {
                Id = Guid.NewGuid(),
                Name = Conversation.RequiredText(request.Name, 100, "Le nom"),
                SystemPrompt = Conversation.RequiredText(request.SystemPrompt, 4000, "Le prompt")
            };
            if (await database.PromptProfiles.CountAsync(ct) >= 200)
                throw new ArgumentException("Bibliothèque limitée à 200 profils.");
            database.PromptProfiles.Add(profile);
            await database.SaveChangesAsync(ct);
            return Results.Created($"/api/chat/prompts/{profile.Id}", profile);
        });
        routes.MapDelete("/prompts/{id:guid}", async (Guid id, ChatDbContext database, CancellationToken ct) =>
        {
            await database.PromptProfiles.Where(p => p.Id == id).ExecuteDeleteAsync(ct);
            return Results.NoContent();
        });
        routes.MapPut("/prompts/{id:guid}", async (Guid id, SavePrompt request, ChatDbContext database, CancellationToken ct) =>
        {
            var profile = await database.PromptProfiles.FindAsync([id], ct);
            if (profile is null) return Results.NotFound();
            profile.Name = Conversation.RequiredText(request.Name, 100, "Le nom");
            profile.SystemPrompt = Conversation.RequiredText(request.SystemPrompt, 4000, "Le prompt");
            await database.SaveChangesAsync(ct);
            return Results.Ok(profile);
        });
    }

    private sealed record ConfigureConversation(int Revision, string Model, string? SystemPrompt,
        string? ContextNotes, ChatOptions? Options);
    private sealed record PreviewContext(string? Content);
    private sealed record ConfigureFile(int Revision, string Mode);
    private sealed record SavePrompt(string? Name, string? SystemPrompt);
}
