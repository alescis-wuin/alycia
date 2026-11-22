using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Alycia.Modules.Documents.Infrastructure;

public sealed class ConversationFiles(DbContext database, FileStorage storage, IConversationFileChanges conversations, ILogger<ConversationFiles> logger)
{
    public Task<List<FileSummary>> ForHistoryAsync(Guid id, int minimum, int? before, CancellationToken ct) =>
        database.Set<Attachment>().AsNoTracking().Where(f => f.ConversationId == id
            && f.AnchorSequence >= minimum && (before == null || f.AnchorSequence < before))
            .Select(f => new FileSummary(f.Id, f.Name, f.Size, f.Status, f.AnchorSequence,
                f.CreatedAtUtc, f.ArchiveId, f.Sha256, f.ContextMode, f.BatchId, f.SourceCreatedAt, f.SourceModifiedAt, f.MetadataSource, f.WorkspaceId)).ToListAsync(ct);

    public async Task<bool> RemoveAsync(Guid id, Guid fileId, CancellationToken ct)
    {
        var conversation = await conversations.RequireAsync(id, ct);
        var all = await database.Set<Attachment>().Where(f => f.ConversationId == id).ToListAsync(ct);
        if (!all.Any(f => f.Id == fileId)) return false;
        var ids = new HashSet<Guid> { fileId };
        for (var changed = true; changed;)
        {
            changed = false;
            foreach (var file in all)
                if (file.ArchiveId.HasValue && ids.Contains(file.ArchiveId.Value)) changed |= ids.Add(file.Id);
        }
        var files = all.Where(f => ids.Contains(f.Id)).ToArray();
        database.Set<Attachment>().RemoveRange(files);
        await conversations.StageAdvanceAsync(id, conversation.Revision, ct);
        await database.SaveVersionedAsync(ct);
        foreach (var file in files)
        {
            try { File.Delete(storage.PathFor(id, file.Id)); }
            catch (IOException) { logger.LogWarning("File cleanup pending: {Id}", file.Id); }
            catch (UnauthorizedAccessException) { logger.LogWarning("File cleanup denied: {Id}", file.Id); }
        }
        return true;
    }
}
