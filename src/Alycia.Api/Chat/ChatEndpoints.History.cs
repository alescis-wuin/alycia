using System.Text;
using Alycia.Modules.Chat.Application;
using Alycia.Modules.Chat.Domain;
using Alycia.Modules.Chat.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Alycia.Api.Chat;

internal static partial class ChatEndpoints
{
    private static void MapHistory(RouteGroupBuilder routes)
    {
        routes.MapGet("/search", async (string? q, int? offset, Guid? workspaceId, bool? unassigned, ChatDbContext database, CancellationToken ct) =>
        {
            var query = q?.Trim() ?? "";
            if (query.Length > 200 || offset is < 0 or > 100000) throw new ArgumentException("Recherche invalide.");
            var pattern = "%" + query.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
            var matches = database.Conversations.AsNoTracking().Where(c => query == "" || EF.Functions.ILike(c.Title, pattern, "\\")
                || database.Messages.Any(m => m.ConversationId == c.Id && EF.Functions.ILike(m.Content, pattern, "\\")));
            if (workspaceId.HasValue || unassigned == true) matches = matches.Where(c => c.WorkspaceId == workspaceId);
            var items = await matches.OrderByDescending(c => c.UpdatedAtUtc).ThenBy(c => c.Id)
                .Skip(offset ?? 0).Take(51).ToListAsync(ct);
            return Results.Ok(new { items = items.Take(50), hasMore = items.Count > 50 });
        });

        routes.MapGet("/conversations/{id:guid}/export", async (Guid id, string? format,
            ChatDbContext database, ChatOptions defaults, HttpContext http, CancellationToken ct) =>
        {
            var archive = await ArchiveAsync(id, null, database, defaults, ct);
            // Same portable size bound for either representation.
            var json = ConversationTransfer.ExportJson(archive);
            http.Response.Headers.CacheControl = "no-store";
            return format switch
            {
                null or "json" => Results.File(json, "application/json", $"alycia-{id:N}.json"),
                "markdown" => Results.File(Encoding.UTF8.GetBytes(ConversationTransfer.Markdown(archive)),
                    "text/markdown; charset=utf-8", $"alycia-{id:N}.md"),
                _ => throw new ArgumentException("Format d’export attendu : json ou markdown.")
            };
        });

        routes.MapPost("/import", async (HttpRequest request, ChatDbContext database, CancellationToken ct) =>
        {
            if (request.ContentLength > ConversationTransfer.MaxBytes) throw new ArgumentException("Import limité à 5 Mio.");
            var bytes = await FileImport.ReadBoundedAsync(request.Body, ConversationTransfer.MaxBytes, ct);
            var restored = ConversationTransfer.Restore(ConversationTransfer.Parse(bytes));
            database.Conversations.Add(restored.Conversation);
            database.Messages.AddRange(restored.Messages);
            await database.SaveChangesAsync(ct);
            return Results.Created($"/api/chat/conversations/{restored.Conversation.Id}", restored.Conversation);
        });

        routes.MapPost("/conversations/{id:guid}/fork", async (Guid id, ForkConversation request,
            ChatDbContext database, ChatOptions defaults, FileStorage storage, CancellationToken ct) =>
        {
            var original = await database.Conversations.AsNoTracking().SingleOrDefaultAsync(c => c.Id == id, ct)
                ?? throw new ConversationNotFoundException();
            ConversationConfiguration.CheckRevision(original, request.Revision);
            var through = request.ThroughSequence ?? original.MessageCount;
            if (through < 0 || through > original.MessageCount || through % 2 != 0)
                throw new ArgumentException("Une copie doit s’arrêter après un échange complet.");
            var archive = await ArchiveAsync(id, through, database, defaults, ct);
            archive = archive with { Title = ("Copie · " + archive.Title)[..Math.Min(100, archive.Title.Length + 8)] };
            var restored = ConversationTransfer.Restore(archive);
            restored.Conversation.AssignWorkspace(original.WorkspaceId);
            var files = await database.Attachments.AsNoTracking().Where(f => f.ConversationId == id
                && f.AnchorSequence <= through + 1).ToListAsync(ct);
            var ids = files.ToDictionary(f => f.Id, _ => Guid.NewGuid());
            var copies = new List<Attachment>();
            var committing = false;
            try
            {
                if (files.Count > 0) Directory.CreateDirectory(storage.DirectoryFor(restored.Conversation.Id));
                foreach (var file in files)
                {
                    var newId = ids[file.Id];
                    await using var source = File.OpenRead(storage.PathFor(id, file.Id));
                    await using var target = File.Create(storage.PathFor(restored.Conversation.Id, newId));
                    await source.CopyToAsync(target, ct);
                    copies.Add(new Attachment
                    {
                        Id = newId,
                        ConversationId = restored.Conversation.Id,
                        BatchId = restored.Conversation.Id,
                        ArchiveId = file.ArchiveId.HasValue ? ids.GetValueOrDefault(file.ArchiveId.Value) : null,
                        Name = file.Name,
                        Text = file.Text,
                        Status = file.Status,
                        Size = file.Size,
                        Sha256 = file.Sha256,
                        CreatedAtUtc = file.CreatedAtUtc,
                        AnchorSequence = file.AnchorSequence,
                        SourceCreatedAt = file.SourceCreatedAt,
                        SourceModifiedAt = file.SourceModifiedAt,
                        MetadataSource = file.MetadataSource,
                        ContextMode = file.ContextMode
                    });
                }
                // Detect changes while the file copies were being prepared.
                if (!await database.Conversations.AnyAsync(c => c.Id == id && c.Revision == request.Revision, ct))
                    throw new ChatConflictException();
                database.Conversations.Add(restored.Conversation);
                if (original.WorkspaceId.HasValue)
                {
                    var localModes = await database.WorkspaceFileOverrides.AsNoTracking().Where(o => o.ConversationId == id).ToArrayAsync(ct);
                    database.WorkspaceFileOverrides.AddRange(localModes.Select(o => new WorkspaceFileOverride
                    { ConversationId = restored.Conversation.Id, FileId = o.FileId, Mode = o.Mode }));
                }
                database.Messages.AddRange(restored.Messages);
                database.Attachments.AddRange(copies);
                committing = true;
                await database.SaveChangesAsync(ct);
                return Results.Created($"/api/chat/conversations/{restored.Conversation.Id}", restored.Conversation);
            }
            catch
            {
                if (!committing) storage.DeleteConversation(restored.Conversation.Id);
                throw;
            }
        });
    }

    private static async Task<ConversationArchive> ArchiveAsync(Guid id, int? through, ChatDbContext database,
        ChatOptions defaults, CancellationToken ct)
    {
        var conversation = await database.Conversations.AsNoTracking().SingleOrDefaultAsync(c => c.Id == id, ct)
            ?? throw new ConversationNotFoundException();
        var limit = through ?? conversation.MessageCount;
        if (limit > ConversationTransfer.MaxMessages) throw new ArgumentException("Export limité à 2000 messages : utilisez la sauvegarde complète.");
        var query = database.Messages.AsNoTracking().Where(m => m.ConversationId == id && m.Sequence <= limit);
        if (await query.SumAsync(m => (long)m.Content.Length + m.ToolTraceJson.Length + m.DiagnosticsJson.Length, ct) > ConversationTransfer.MaxBytes)
            throw new ArgumentException("Export trop volumineux : utilisez la sauvegarde complète.");
        var messages = await query.OrderBy(m => m.Sequence)
            .Select(m => new ArchivedMessage(m.Role, m.Content, m.CreatedAtUtc, m.ToolTraceJson, m.DiagnosticsJson)).ToArrayAsync(ct);
        if (messages.Length != limit) throw new ChatConflictException();
        return new(1, conversation.Title, conversation.Model, conversation.SystemPrompt, conversation.ContextNotes,
            ConversationConfiguration.Options(conversation, defaults), messages, conversation.WorkspaceId,
            conversation.WorkspaceId is Guid project ? await database.Attachments.AsNoTracking().Where(f => f.WorkspaceId == project)
                .OrderBy(f => f.Name).Select(f => f.Name + " · " + f.Sha256).ToArrayAsync(ct) : null);
    }

    private sealed record ForkConversation(int Revision, int? ThroughSequence);
}
