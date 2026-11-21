using Microsoft.EntityFrameworkCore;

namespace Alycia.Modules.Chat.Infrastructure;

public sealed class ConversationOriginals(ChatDbContext database, FileStorage storage, DocumentScope scope) : IConversationOriginals
{
    public async Task<DownloadedOriginal?> DownloadAsync(Guid conversation, Guid file, CancellationToken ct)
    {
        var metadata = await scope.ReadAsync(database, conversation, false, false, q => q.Where(f => f.Id == file && f.ContextMode != "excluded")
            .Select(f => new { f.Id, f.Name, f.Size, f.Sha256, f.WorkspaceId }).SingleOrDefaultAsync(ct), ct);
        if (metadata is null) return null;
        Stream? content = null;
        try
        {
            var snapshot = await VerifiedOriginal.OpenAsync(storage, metadata.WorkspaceId ?? conversation,
                metadata.WorkspaceId.HasValue, file, metadata.Size, metadata.Sha256, ct);
            content = snapshot.Content;
            if (!await scope.ReadAsync(database, conversation, false, false, q => q.AnyAsync(f => f.Id == file && f.ContextMode != "excluded" && f.Sha256 == metadata.Sha256, ct), ct)) return null;
            var result = new DownloadedOriginal(content, Path.GetFileName(metadata.Name), snapshot.Modified);
            content = null;
            return result;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            throw new OriginalUnavailableException(ex);
        }
        finally { if (content is not null) await content.DisposeAsync(); }
    }
}
