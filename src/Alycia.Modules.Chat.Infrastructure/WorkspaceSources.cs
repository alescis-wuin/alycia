using Alycia.Modules.Chat.Domain;
using Microsoft.EntityFrameworkCore;

namespace Alycia.Modules.Chat.Infrastructure;

public static class WorkspaceSources
{
    // Every retrieval, analysis and tool read starts from the same membership predicate.
    // A detached projection carries the effective mode without modifying the shared original.
    public static IQueryable<Attachment> ForConversation(this ChatDbContext db, Guid id) =>
        from file in db.Attachments.AsNoTracking()
        where file.ConversationId == id || (file.WorkspaceId != null
            && db.Conversations.Any(c => c.Id == id && c.WorkspaceId == file.WorkspaceId))
        let local = db.WorkspaceFileOverrides.Where(o => o.ConversationId == id && o.FileId == file.Id)
            .Select(o => o.Mode).FirstOrDefault()
        select new Attachment
        {
            Id = file.Id, ConversationId = file.ConversationId, WorkspaceId = file.WorkspaceId,
            Name = file.Name, Size = file.Size, Sha256 = file.Sha256, Status = file.Status, Text = file.Text,
            ContextMode = file.ContextMode == "excluded" ? "excluded" : local ?? file.ContextMode,
            BatchId = file.BatchId, ArchiveId = file.ArchiveId, AnchorSequence = file.AnchorSequence,
            CreatedAtUtc = file.CreatedAtUtc, SourceCreatedAt = file.SourceCreatedAt,
            SourceModifiedAt = file.SourceModifiedAt, MetadataSource = file.MetadataSource
        };
}
