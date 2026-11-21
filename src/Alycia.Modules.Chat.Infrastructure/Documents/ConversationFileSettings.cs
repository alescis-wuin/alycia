using Alycia.Modules.Chat.Application;
using Microsoft.EntityFrameworkCore;

namespace Alycia.Modules.Chat.Infrastructure;

public sealed class ConversationFileSettings(ChatDbContext database, IConversationFileChanges conversations, DocumentScope scope)
{
    public async Task<IReadOnlyList<FileSummary>> ListAsync(Guid id, CancellationToken ct)
    {
        await conversations.RequireAsync(id, ct);
        // Project metadata in SQL: do not load extracted documents for this list.
        return await scope.ReadAsync(database, id, false, false, q => q.OrderBy(f => f.CreatedAtUtc).Select(f => new FileSummary(f.Id, f.Name, f.Size, f.Status,
            f.AnchorSequence, f.CreatedAtUtc, f.ArchiveId, f.Sha256, f.ContextMode, f.BatchId, f.SourceCreatedAt, f.SourceModifiedAt, f.MetadataSource, f.WorkspaceId)).ToListAsync(ct), ct);
    }

    public async Task<FileSummary> SetModeAsync(Guid id, Guid fileId, int revision, string mode, CancellationToken ct)
    {
        if (mode is not ("window" or "pinned" or "excluded" or "project")) throw new ArgumentException("Mode documentaire invalide.");
        var conversation = await conversations.RequireAsync(id, ct);
        if (conversation.Revision != revision) throw new ChatConflictException();
        if (conversation.WorkspaceId is Guid project && await database.Attachments.AnyAsync(f => f.Id == fileId && f.WorkspaceId == project, ct))
        {
            if (mode == "window") throw new ArgumentException("Une source du projet est disponible, épinglée ou exclue.");
            var entry = await database.WorkspaceFileOverrides.FindAsync([id, fileId], ct);
            if (entry is null) database.WorkspaceFileOverrides.Add(new() { ConversationId = id, FileId = fileId, Mode = mode });
            else entry.Mode = mode;
            await conversations.StageAdvanceAsync(id, revision, ct);
            await database.SaveVersionedAsync(ct);
            return FileImport.Summary(await scope.ReadAsync(database, id, false, false, q => q.SingleAsync(f => f.Id == fileId, ct), ct));
        }
        var file = await database.Attachments.SingleOrDefaultAsync(f => f.ConversationId == id && f.Id == fileId, ct)
            ?? throw new ArgumentException("Fichier introuvable.");
        file.ContextMode = mode;
        await conversations.StageAdvanceAsync(id, revision, ct);
        await database.SaveVersionedAsync(ct);
        return FileImport.Summary(file);
    }
}
