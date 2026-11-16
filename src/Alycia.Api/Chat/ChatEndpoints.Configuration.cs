using Alycia.Modules.Chat.Application;
using Alycia.Modules.Chat.Domain;
using Alycia.Modules.Chat.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Alycia.Api.Chat;

internal static partial class ChatEndpoints
{
    private static void MapConfiguration(RouteGroupBuilder routes)
    {
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
            IChatRepository repository, IChatModel model, ChatOptions defaults, CancellationToken ct) =>
        {
            var conversation = await repository.FindAsync(id, ct) ?? throw new ConversationNotFoundException();
            var options = ConversationConfiguration.Options(conversation, defaults);
            var text = Conversation.RequiredText(request.Content, 32000, "Le message");
            ContextWindow context;
            if (options.AutoSummarize)
                context = (await RecursiveContext.BuildAsync(repository, model, conversation, text, options, false, null, ct)).Context;
            else
            {
                var history = await repository.MessagesAsync(id, options.MaxHistoryTurns * 2, null, ct);
                var files = await repository.ContextFilesAsync(id, Math.Max(1, conversation.MessageCount - options.MaxHistoryTurns * 2 + 1), ct);
                context = FileContext.Build(ConversationConfiguration.System(conversation), history, text, options, files, conversation.MessageCount + 1);
            }
            return Results.Ok(new
            {
                context,
                options,
                conversation.Revision,
                omittedMessages = conversation.MessageCount - context.HistoryMessagesIncluded,
                estimator = "UTF-8 conservateur ; estimation, pas un tokenizer du modèle"
            });
        });

        routes.MapGet("/conversations/{id:guid}/files", async (Guid id, ChatDbContext database, CancellationToken ct) =>
        {
            if (!await database.Conversations.AnyAsync(c => c.Id == id, ct)) throw new ConversationNotFoundException();
            // Project metadata in SQL: do not load extracted documents for this list.
            return Results.Ok(await database.Attachments.AsNoTracking().Where(f => f.ConversationId == id)
                .OrderBy(f => f.CreatedAtUtc).Select(f => new FileSummary(f.Id, f.Name, f.Size, f.Status,
                    f.AnchorSequence, f.CreatedAtUtc, f.ArchiveId, f.Sha256, f.ContextMode, f.BatchId, f.SourceCreatedAt, f.SourceModifiedAt, f.MetadataSource)).ToListAsync(ct));
        });

        routes.MapPatch("/conversations/{id:guid}/files/{fileId:guid}", async (Guid id, Guid fileId,
            ConfigureFile request, ChatDbContext database, IChatRepository repository, CancellationToken ct) =>
        {
            if (request.Mode is not ("window" or "pinned" or "excluded"))
                throw new ArgumentException("Mode documentaire invalide.");
            var conversation = await repository.FindAsync(id, ct) ?? throw new ConversationNotFoundException();
            ConversationConfiguration.CheckRevision(conversation, request.Revision);
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
