using System.Data;
using Alycia.Platform.Persistence;
using Alycia.Modules.Chat.Domain;
using Microsoft.EntityFrameworkCore;

namespace Alycia.Modules.Chat.Infrastructure;

public sealed class DocumentScope(Func<RelationalSession, IConversationWorkspaceLookup> membership)
{
    internal async Task<T> ReadAsync<T>(ChatDbContext db, Guid id, bool project, bool model,
        Func<IQueryable<Attachment>, Task<T>> read, CancellationToken ct)
    {
        if (project) return await read(db.Attachments.AsNoTracking().Where(f => f.WorkspaceId == id));
        await using var snapshot = db.Database.CurrentTransaction is null
            ? await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct) : null;
        var workspace = await membership(new RelationalSession(db.Database)).FindWorkspaceAsync(id, ct);
        var files = ForConversation(db, id, workspace);
        if (model)
            files = files.Where(file => file.WorkspaceId != null || !db.Attachments.Any(other =>
                workspace.HasValue && other.WorkspaceId == workspace && other.Name == file.Name && other.Sha256 == file.Sha256));
        var result = await read(files);
        if (snapshot is not null) await snapshot.CommitAsync(ct);
        return result;
    }

    // Every retrieval, analysis and tool read starts from the same membership predicate.
    // A detached projection carries the effective mode without modifying the shared original.
    private static IQueryable<Attachment> ForConversation(ChatDbContext db, Guid id, Guid? workspace) =>
        from file in db.Attachments.AsNoTracking()
        where file.ConversationId == id || (workspace.HasValue && file.WorkspaceId == workspace)
        let local = db.WorkspaceFileOverrides.Where(o => o.ConversationId == id && o.FileId == file.Id)
            .Select(o => o.Mode).FirstOrDefault()
        select new Attachment
        {
            Id = file.Id, ConversationId = file.ConversationId, WorkspaceId = file.WorkspaceId,
            Name = file.Name, Size = file.Size, Sha256 = file.Sha256, Status = file.Status, Text = file.Text,
            ContextMode = file.ContextMode == "excluded" || (file.WorkspaceId != null && db.Attachments.Any(other =>
                other.ConversationId == id && other.Name == file.Name && other.Sha256 == file.Sha256 && other.ContextMode == "excluded"))
                ? "excluded" : local ?? file.ContextMode,
            BatchId = file.BatchId, ArchiveId = file.ArchiveId, AnchorSequence = file.AnchorSequence,
            CreatedAtUtc = file.CreatedAtUtc, SourceCreatedAt = file.SourceCreatedAt,
            SourceModifiedAt = file.SourceModifiedAt, MetadataSource = file.MetadataSource
        };

}
