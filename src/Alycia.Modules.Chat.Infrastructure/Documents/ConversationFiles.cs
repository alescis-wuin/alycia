using Alycia.Modules.Chat.Application;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Alycia.Modules.Chat.Infrastructure;

public sealed class ConversationFiles(ChatDbContext database, FileStorage storage, ILogger<ConversationFiles> logger)
{
    public Task<List<FileSummary>> ForHistoryAsync(Guid id, int minimum, int? before, CancellationToken ct) =>
        database.Attachments.AsNoTracking().Where(f => f.ConversationId == id
            && f.AnchorSequence >= minimum && (before == null || f.AnchorSequence < before))
            .Select(f => new FileSummary(f.Id, f.Name, f.Size, f.Status, f.AnchorSequence,
                f.CreatedAtUtc, f.ArchiveId, f.Sha256, f.ContextMode, f.BatchId, f.SourceCreatedAt, f.SourceModifiedAt, f.MetadataSource, f.WorkspaceId)).ToListAsync(ct);

    public async Task<(string Path, string Name)?> DownloadAsync(Guid id, Guid fileId, CancellationToken ct)
    {
        var file = await database.ForConversation(id).SingleOrDefaultAsync(f => f.Id == fileId, ct);
        return file is null ? null : (storage.PathFor(file), Path.GetFileName(file.Name));
    }

    public async Task<bool> RemoveAsync(Guid id, Guid fileId, CancellationToken ct)
    {
        var conversation = await database.Conversations.SingleOrDefaultAsync(c => c.Id == id, ct)
            ?? throw new ConversationNotFoundException();
        var all = await database.Attachments.Where(f => f.ConversationId == id).ToListAsync(ct);
        if (!all.Any(f => f.Id == fileId)) return false;
        var ids = new HashSet<Guid> { fileId };
        for (var changed = true; changed;)
        {
            changed = false;
            foreach (var file in all)
                if (file.ArchiveId.HasValue && ids.Contains(file.ArchiveId.Value)) changed |= ids.Add(file.Id);
        }
        var files = all.Where(f => ids.Contains(f.Id)).ToArray();
        database.Attachments.RemoveRange(files);
        conversation.AttachFiles();
        await new EfChatRepository(database).SaveAsync(ct);
        foreach (var file in files)
        {
            try { File.Delete(storage.PathFor(id, file.Id)); }
            catch (IOException) { logger.LogWarning("File cleanup pending: {Id}", file.Id); }
            catch (UnauthorizedAccessException) { logger.LogWarning("File cleanup denied: {Id}", file.Id); }
        }
        return true;
    }
}
