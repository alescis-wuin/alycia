using System.Text.Json;
using Alycia.Modules.Chat.Application;
using Alycia.Modules.Chat.Domain;
using Alycia.Modules.Chat.Infrastructure;
using Alycia.Modules.Workspaces.Domain;
using Microsoft.EntityFrameworkCore;

namespace Alycia.Api.Chat;

internal static partial class ChatEndpoints
{
    private sealed record ProjectName(Guid Id, string Name);
    private sealed record EditProject(int Revision, string Instructions, string DefaultModel, bool Archived);
    private sealed record ProjectMembership(int Revision, Guid? WorkspaceId);
    private sealed record SetProjectMode(string Mode, int Revision);
    private sealed record NoteEdit(int Revision, string Text, Guid? OriginConversationId = null, int? OriginSequence = null);
    private sealed record NewProject(string Name);

    private static object Profile(WorkspaceProfile p) => new { p.Id, p.Instructions, p.DefaultModel, p.Archived, p.Revision, notes = p.ReadNotes() };

    private static void CheckProjectRevision(WorkspaceProfile profile, int revision)
    {
        if (profile.Revision != revision) throw new ChatConflictException();
    }

    private static void MapProjects(RouteGroupBuilder routes)
    {
        routes.MapGet("/projects", async (ChatDbContext db, CancellationToken ct) =>
        {
            var names = await db.Database.SqlQuery<ProjectName>($"""SELECT id AS "Id", name::text AS "Name" FROM workspaces.workspaces ORDER BY name""").ToArrayAsync(ct);
            var profiles = await db.WorkspaceProfiles.AsNoTracking().ToDictionaryAsync(p => p.Id, ct);
            return Results.Ok(names.Select(n => new { n.Id, n.Name, archived = profiles.GetValueOrDefault(n.Id)?.Archived ?? false }));
        });
        routes.MapPost("/projects", async (NewProject request, ChatDbContext db, CancellationToken ct) =>
        {
            var workspace = Workspace.Create(request.Name);
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var rows = await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO workspaces.workspaces (id, name, created_at_utc) VALUES ({workspace.Id}, {workspace.Name}, {workspace.CreatedAtUtc})
                ON CONFLICT (name) DO NOTHING
                """, ct);
            if (rows == 0) throw new ArgumentException("Ce nom de projet existe déjà.");
            db.WorkspaceProfiles.Add(new() { Id = workspace.Id });
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return Results.Ok(new { workspace.Id, workspace.Name, archived = false });
        });
        routes.MapGet("/projects/{id:guid}", async (Guid id, ChatDbContext db, CancellationToken ct) =>
            Results.Ok(Profile(await ProjectProfiles.RequireAsync(db, id, ct, true))));
        routes.MapPut("/projects/{id:guid}", async (Guid id, EditProject update, ChatDbContext db, CancellationToken ct) =>
        {
            var project = await ProjectProfiles.RequireAsync(db, id, ct, true);
            CheckProjectRevision(project, update.Revision);
            if (update.Instructions is null || update.Instructions.Length > 4000 || update.DefaultModel is null
                || update.DefaultModel.Length > 200 || update.Instructions.Contains('\0') || update.DefaultModel.Contains('\0'))
                throw new ArgumentException("Consignes ou modèle par défaut invalides.");
            project.Instructions = update.Instructions.Trim();
            project.DefaultModel = update.DefaultModel.Trim();
            project.Archived = update.Archived;
            project.Revision++;
            await new EfChatRepository(db).SaveAsync(ct);
            return Results.Ok(Profile(project));
        });
        routes.MapPut("/conversations/{id:guid}/project", async (Guid id, ProjectMembership update, ChatDbContext db, CancellationToken ct) =>
        {
            using var gate = GenerationGate.Shared.Enter(id);
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var chat = await db.Conversations.SingleOrDefaultAsync(c => c.Id == id, ct) ?? throw new ConversationNotFoundException();
            ConversationConfiguration.CheckRevision(chat, update.Revision);
            if (update.WorkspaceId is Guid destination) await ProjectProfiles.RequireAsync(db, destination, ct);
            if (chat.WorkspaceId != update.WorkspaceId)
            {
                await db.WorkspaceFileOverrides.Where(o => o.ConversationId == id).ExecuteDeleteAsync(ct);
                chat.AssignWorkspace(update.WorkspaceId);
                await new EfChatRepository(db).SaveAsync(ct);
            }
            await tx.CommitAsync(ct);
            return Results.Ok(chat);
        });
        routes.MapPost("/projects/{id:guid}/notes", (Guid id, NoteEdit update, ChatDbContext db, CancellationToken ct) =>
            ChangeNoteAsync(id, null, update, false, db, ct));
        routes.MapPut("/projects/{id:guid}/notes/{note:guid}", (Guid id, Guid note, NoteEdit update, ChatDbContext db, CancellationToken ct) =>
            ChangeNoteAsync(id, note, update, false, db, ct));
        routes.MapDelete("/projects/{id:guid}/notes/{note:guid}", (Guid id, Guid note, int revision, ChatDbContext db, CancellationToken ct) =>
            ChangeNoteAsync(id, note, new(revision, ""), true, db, ct));

        routes.MapPost("/conversations/{id:guid}/files/{fileId:guid}/share", async (Guid id, Guid fileId, ChatDbContext db,
            FileImport importer, FileStorage storage, CancellationToken ct) =>
        {
            var conversation = await db.Conversations.AsNoTracking().SingleOrDefaultAsync(c => c.Id == id, ct) ?? throw new ConversationNotFoundException();
            var project = conversation.WorkspaceId ?? throw new ArgumentException("Rattachez la conversation à un projet.");
            var file = await db.Attachments.AsNoTracking().SingleOrDefaultAsync(f => f.ConversationId == id && f.Id == fileId, ct)
                ?? throw new ArgumentException("Pièce jointe introuvable.");
            if (file.ContextMode == "excluded") throw new ArgumentException("Une source exclue ne peut pas être promue.");
            await using var original = File.OpenRead(storage.PathFor(file));
            var result = await importer.ImportAsync(project, file.Id, [new(file.Name, original, file.SourceCreatedAt,
                file.SourceModifiedAt, file.MetadataSource)], ct, workspace: true);
            return Results.Ok(result);
        });
        routes.MapGet("/projects/{id:guid}/files", async (Guid id, ChatDbContext db, CancellationToken ct) =>
        {
            await ProjectProfiles.RequireAsync(db, id, ct, true);
            return Results.Ok(await FileSummaries(db.Attachments.AsNoTracking().Where(f => f.WorkspaceId == id)).ToArrayAsync(ct));
        });
        routes.MapPost("/projects/{id:guid}/files/{batch:guid}", async (Guid id, Guid batch, HttpRequest request, FileImport importer, ChatDbContext db, CancellationToken ct) =>
        {
            await ProjectProfiles.RequireAsync(db, id, ct);
            return await UploadFilesAsync(id, batch, request, importer, true, ct);
        });
        routes.MapPut("/projects/{id:guid}/files/{fileId:guid}", async (Guid id, Guid fileId, SetProjectMode update, ChatDbContext db, CancellationToken ct) =>
        {
            var profile = await ProjectProfiles.RequireAsync(db, id, ct);
            CheckProjectRevision(profile, update.Revision);
            var file = await db.Attachments.SingleOrDefaultAsync(f => f.WorkspaceId == id && f.Id == fileId, ct)
                ?? throw new ArgumentException("Document absent de ce projet.");
            if (update.Mode is not ("project" or "pinned" or "excluded")) throw new ArgumentException("Mode de source invalide.");
            if (ImportPolicy.Excluded(file.Name) && update.Mode != "excluded") throw new ArgumentException("Cette source est exclue par la politique d’import.");
            file.ContextMode = update.Mode;
            profile.Revision++;
            await new EfChatRepository(db).SaveAsync(ct);
            return Results.Ok(FileImport.Summary(file));
        });
        routes.MapDelete("/projects/{id:guid}/files/{fileId:guid}", async (Guid id, Guid fileId, int revision, ChatDbContext db, CancellationToken ct) =>
        {
            var profile = await ProjectProfiles.RequireAsync(db, id, ct);
            CheckProjectRevision(profile, revision);
            var owned = await db.Attachments.Where(f => f.WorkspaceId == id).Select(f => new { f.Id, f.ArchiveId }).ToArrayAsync(ct);
            if (!owned.Any(f => f.Id == fileId)) return Results.NotFound();
            var removed = new HashSet<Guid> { fileId };
            for (int count = -1; count != removed.Count;)
            {
                count = removed.Count;
                foreach (var f in owned) if (f.ArchiveId is Guid parent && removed.Contains(parent)) removed.Add(f.Id);
            }
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            await db.Attachments.Where(f => f.WorkspaceId == id && removed.Contains(f.Id)).ExecuteDeleteAsync(ct);
            profile.Revision++;
            await new EfChatRepository(db).SaveAsync(ct);
            await tx.CommitAsync(ct);
            // Retain immutable originals for already running snapshot reads and backups.
            return Results.NoContent();
        });
        routes.MapDelete("/conversations/{id:guid}/overrides/{fileId:guid}", async (Guid id, Guid fileId, int revision, ChatDbContext db, CancellationToken ct) =>
        {
            var chat = await db.Conversations.SingleOrDefaultAsync(c => c.Id == id, ct) ?? throw new ConversationNotFoundException();
            ConversationConfiguration.CheckRevision(chat, revision);
            var rule = await db.WorkspaceFileOverrides.FindAsync([id, fileId], ct);
            if (rule is not null) { db.Remove(rule); chat.AttachFiles(); await new EfChatRepository(db).SaveAsync(ct); }
            return Results.NoContent();
        });
        routes.MapGet("/projects/{id:guid}/files/{fileId:guid}/preview", (Guid id, Guid fileId, ChatDbContext db, ProjectSourceReader reader, CancellationToken ct) =>
            SourcePreviewAsync(db.Attachments.AsNoTracking().Where(f => f.WorkspaceId == id), fileId, null, null, null, reader, ct));
        routes.MapGet("/conversations/{id:guid}/files/{fileId:guid}/preview", (Guid id, Guid fileId, string? sha, int? start, int? end,
            ChatDbContext db, ProjectSourceReader reader, CancellationToken ct) =>
            SourcePreviewAsync(db.ForConversation(id), fileId, sha, start, end, reader, ct));
    }

    private static IQueryable<FileSummary> FileSummaries(IQueryable<Attachment> files) =>
        files.OrderBy(f => f.Name).ThenBy(f => f.Id).Select(f => new FileSummary(f.Id, f.Name, f.Size, f.Status, f.AnchorSequence,
            f.CreatedAtUtc, f.ArchiveId, f.Sha256, f.ContextMode, f.BatchId, f.SourceCreatedAt, f.SourceModifiedAt, f.MetadataSource, f.WorkspaceId));

    private static async Task<IResult> SourcePreviewAsync(IQueryable<Attachment> files, Guid fileId, string? sha, int? start, int? end,
        ProjectSourceReader reader, CancellationToken ct)
    {
        var file = await files.SingleOrDefaultAsync(f => f.Id == fileId, ct);
        if (file is null) return Results.NotFound();
        if (sha is not null && !string.Equals(sha, file.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Cette version de la source est indisponible.");
        var source = await reader.ReadAsync(file, ct);
        int from = start ?? 0;
        if (from < 0 || from > source.Text.Length || end.HasValue && end < from) throw new ArgumentException("Position source invalide.");
        int through = Math.Min(Math.Min(end ?? source.Text.Length, source.Text.Length), from + 12000);
        return Results.Ok(new
        {
            file.Name,
            file.Sha256,
            text = source.Text[from..through],
            start = from,
            end = through,
            partial = !source.Complete || from > 0 || through < source.Text.Length
        });
    }

    private static async Task<IResult> ChangeNoteAsync(Guid id, Guid? key, NoteEdit edit, bool remove, ChatDbContext db, CancellationToken ct)
    {
        var profile = await ProjectProfiles.RequireAsync(db, id, ct);
        CheckProjectRevision(profile, edit.Revision);
        var notes = profile.ReadNotes().ToList();
        int index = notes.FindIndex(n => n.Id == key);
        if (key.HasValue && index < 0) throw new ArgumentException("Note introuvable.");
        if (remove) notes.RemoveAt(index);
        else
        {
            string text = Conversation.RequiredText(edit.Text, 1500, "La note");
            if (edit.OriginConversationId is Guid origin && !await db.Conversations.AnyAsync(c => c.Id == origin && c.WorkspaceId == id, ct))
                throw new ArgumentException("Cette conversation ne fait pas partie du projet.");
            if (edit.OriginSequence.HasValue && !await db.Messages.AnyAsync(m => m.ConversationId == edit.OriginConversationId && m.Sequence == edit.OriginSequence, ct))
                throw new ArgumentException("Message d’origine introuvable.");
            if (index >= 0) notes[index] = notes[index] with { Text = text, UpdatedAtUtc = DateTimeOffset.UtcNow };
            else notes.Add(new(Guid.NewGuid(), text, edit.OriginConversationId, edit.OriginSequence, DateTimeOffset.UtcNow));
            if (notes.Count > 50 || notes.Sum(n => n.Text.Length) > 8000) throw new ArgumentException("Notes du projet limitées à 50 notes et 8 000 caractères.");
        }
        profile.NotesJson = JsonSerializer.Serialize(notes);
        profile.Revision++;
        await new EfChatRepository(db).SaveAsync(ct);
        return Results.Ok(Profile(profile));
    }
}
