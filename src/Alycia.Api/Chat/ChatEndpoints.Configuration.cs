using Alycia.Modules.Chat.Application;
using Alycia.Modules.Chat.Application.Context;
using Alycia.Modules.Chat.Infrastructure;

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

        routes.MapGet("/conversations/{id:guid}/settings", async (Guid id, ConversationSettingsService settings, CancellationToken ct) =>
        {
            var result = await settings.GetAsync(id, ct);
            return Results.Ok(new { conversation = result.Conversation.ToResponse(), result.Options });
        });
        routes.MapPut("/conversations/{id:guid}/settings", async (Guid id, ConfigureConversation request, ConversationSettingsService settings, CancellationToken ct) =>
            Results.Ok((await settings.UpdateAsync(id, request.Revision, request.Model, request.SystemPrompt, request.ContextNotes, request.Options, ct)).ToResponse()));

        routes.MapPost("/conversations/{id:guid}/context", async (Guid id, PreviewContext request, ConversationContextPreparation preparation, CancellationToken ct) =>
        {
            var preview = await preparation.PreviewAsync(id, request.Content, ct);
            return Results.Ok(new { preview.Context, preview.Options, preview.Revision, preview.OmittedMessages, preview.Usage, preview.Estimator });
        });

        routes.MapGet("/conversations/{id:guid}/files", async (Guid id, ConversationFileSettings files, CancellationToken ct) =>
            Results.Ok(await files.ListAsync(id, ct)));
        routes.MapPatch("/conversations/{id:guid}/files/{fileId:guid}", async (Guid id, Guid fileId, ConfigureFile request, ConversationFileSettings files, CancellationToken ct) =>
            Results.Ok(await files.SetModeAsync(id, fileId, request.Revision, request.Mode, ct)));

        routes.MapGet("/prompts", async (PromptCatalog prompts, CancellationToken ct) => Results.Ok(await prompts.ListAsync(ct)));
        routes.MapPost("/prompts", async (SavePrompt request, PromptCatalog prompts, CancellationToken ct) =>
        {
            var profile = await prompts.CreateAsync(request.Name, request.SystemPrompt, ct);
            return Results.Created($"/api/chat/prompts/{profile.Id}", profile);
        });
        routes.MapDelete("/prompts/{id:guid}", async (Guid id, PromptCatalog prompts, CancellationToken ct) =>
        {
            await prompts.DeleteAsync(id, ct);
            return Results.NoContent();
        });
        routes.MapPut("/prompts/{id:guid}", async (Guid id, SavePrompt request, PromptCatalog prompts, CancellationToken ct) =>
        {
            var profile = await prompts.UpdateAsync(id, request.Name, request.SystemPrompt, ct);
            return profile is null ? Results.NotFound() : Results.Ok(profile);
        });
    }

    private sealed record ConfigureConversation(int Revision, string Model, string? SystemPrompt,
        string? ContextNotes, ChatOptions? Options);
    private sealed record PreviewContext(string? Content);
    private sealed record ConfigureFile(int Revision, string Mode);
    private sealed record SavePrompt(string? Name, string? SystemPrompt);
}
