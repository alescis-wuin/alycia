using Alycia.Modules.Chat.Application;
using Alycia.Modules.Chat.Infrastructure;

namespace Alycia.Api.Chat;

internal static partial class ChatEndpoints
{
    private sealed record EditProject(int Revision, string Instructions, string DefaultModel, bool Archived);
    private sealed record ProjectMembership(int Revision, Guid? WorkspaceId);
    private sealed record SetProjectMode(string Mode, int Revision);
    private sealed record NoteEdit(int Revision, string Text, Guid? OriginConversationId = null, int? OriginSequence = null);
    private sealed record NewProject(string Name);

    private static void MapProjects(RouteGroupBuilder routes)
    {
        routes.MapGet("/projects", async (IProjectCatalog projects, CancellationToken ct) =>
            Results.Ok(await projects.ListAsync(ct)));
        routes.MapPost("/projects", async (NewProject request, IProjectCatalog projects, CancellationToken ct) =>
            Results.Ok(await projects.CreateAsync(request.Name, ct)));
        routes.MapGet("/projects/{id:guid}", async (Guid id, IProjectCatalog projects, CancellationToken ct) =>
            Results.Ok(await projects.GetAsync(id, true, ct)));
        routes.MapPut("/projects/{id:guid}", async (Guid id, EditProject update, IProjectCatalog projects, CancellationToken ct) =>
            Results.Ok(await projects.UpdateAsync(id, update.Revision, update.Instructions, update.DefaultModel, update.Archived, ct)));
        routes.MapPut("/conversations/{id:guid}/project", async (Guid id, ProjectMembership update, IProjectCatalog projects, CancellationToken ct) =>
            Results.Ok(await projects.SetMembershipAsync(id, update.Revision, update.WorkspaceId, ct)));
        routes.MapPost("/projects/{id:guid}/notes", async (Guid id, NoteEdit update, IProjectCatalog projects, CancellationToken ct) =>
            Results.Ok(await projects.ChangeNoteAsync(id, null, update.Revision, update.Text, update.OriginConversationId, update.OriginSequence, false, ct)));
        routes.MapPut("/projects/{id:guid}/notes/{note:guid}", async (Guid id, Guid note, NoteEdit update, IProjectCatalog projects, CancellationToken ct) =>
            Results.Ok(await projects.ChangeNoteAsync(id, note, update.Revision, update.Text, update.OriginConversationId, update.OriginSequence, false, ct)));
        routes.MapDelete("/projects/{id:guid}/notes/{note:guid}", async (Guid id, Guid note, int revision, IProjectCatalog projects, CancellationToken ct) =>
            Results.Ok(await projects.ChangeNoteAsync(id, note, revision, "", null, null, true, ct)));

        routes.MapPost("/conversations/{id:guid}/files/{fileId:guid}/share", async (Guid id, Guid fileId, ProjectFiles files, CancellationToken ct) =>
            Results.Ok(await files.ShareAsync(id, fileId, ct)));
        routes.MapGet("/projects/{id:guid}/files", async (Guid id, ProjectFiles files, CancellationToken ct) =>
            Results.Ok(await files.ListAsync(id, ct)));
        routes.MapPost("/projects/{id:guid}/files/{batch:guid}", async (Guid id, Guid batch, HttpRequest request, FileImport importer,
            IProjectCatalog projects, CancellationToken ct) =>
        {
            await projects.ActiveDefaultModelAsync(id, ct);
            return await UploadFilesAsync(id, batch, request, importer, true, ct);
        });
        routes.MapPut("/projects/{id:guid}/files/{fileId:guid}", async (Guid id, Guid fileId, SetProjectMode update, ProjectFiles files, CancellationToken ct) =>
            Results.Ok(await files.SetModeAsync(id, fileId, update.Mode, update.Revision, ct)));
        routes.MapDelete("/projects/{id:guid}/files/{fileId:guid}", async (Guid id, Guid fileId, int revision, ProjectFiles files, CancellationToken ct) =>
            await files.RemoveAsync(id, fileId, revision, ct) ? Results.NoContent() : Results.NotFound());
        routes.MapDelete("/conversations/{id:guid}/overrides/{fileId:guid}", async (Guid id, Guid fileId, int revision, ProjectFiles files, CancellationToken ct) =>
        {
            await files.ClearOverrideAsync(id, fileId, revision, ct);
            return Results.NoContent();
        });
        routes.MapGet("/projects/{id:guid}/files/{fileId:guid}/preview", async (Guid id, Guid fileId, ProjectFiles files, CancellationToken ct) =>
            await files.PreviewAsync(id, true, fileId, null, null, null, ct) is { } result ? Results.Ok(result) : Results.NotFound());
        routes.MapGet("/conversations/{id:guid}/files/{fileId:guid}/preview", async (Guid id, Guid fileId, string? sha, int? start, int? end,
            ProjectFiles files, CancellationToken ct) =>
            await files.PreviewAsync(id, false, fileId, sha, start, end, ct) is { } result ? Results.Ok(result) : Results.NotFound());
    }
}
