using Alycia.Contracts.Workspaces;
using Alycia.Modules.Chat.Application;
using Alycia.Modules.Chat.Domain;
using Alycia.Modules.Chat.Infrastructure.Documents;
using Microsoft.EntityFrameworkCore;

namespace Alycia.Modules.Chat.Infrastructure;

public sealed record SourceFileMetadata(string Name, DateTimeOffset? CreatedAt, DateTimeOffset? ModifiedAt);
public sealed record FileSummary(Guid Id, string Name, long Size, string Status, int AnchorSequence,
    DateTimeOffset CreatedAtUtc, Guid? ArchiveId, string Sha256, string ContextMode = "window", Guid BatchId = default, string SourceCreatedAt = "", string SourceModifiedAt = "", string MetadataSource = "unknown", Guid? WorkspaceId = null);

public sealed class FileImport(ChatDbContext database, FileStorage storage, IWorkspaceProfiles profiles, IConversationFileChanges conversations, ImportSettingsStore? settings = null)
{
    public const int MaxFileBytes = 25 * 1024 * 1024;
    public const int MaxBatchBytes = 100 * 1024 * 1024;
    public const int MaxEntries = 500;
    public const int MaxArchiveDepth = 4;
    public const int MaxTextBytes = DocumentContent.MaxTextBytes;
    public Task<ImportLimits> LimitsAsync(CancellationToken ct) => settings?.LoadAsync(ct) ?? Task.FromResult(new ImportLimits());

    public static FileSummary Summary(Attachment f) => new(f.Id, f.Name, f.Size, f.Status,
        f.AnchorSequence, f.CreatedAtUtc, f.ArchiveId, f.Sha256, f.ContextMode, f.BatchId, f.SourceCreatedAt, f.SourceModifiedAt, f.MetadataSource, f.WorkspaceId);

    public async Task<IReadOnlyList<FileSummary>> ImportAsync(Guid conversationId, Guid batchId,
        IReadOnlyList<IncomingFile> incoming, CancellationToken ct, bool workspace = false)
    {
        var limits = await LimitsAsync(ct);
        var batch = new DocumentImportBatch(batchId, incoming, limits);
        var conversation = workspace ? null : await conversations.RequireAsync(conversationId, ct);
        var profile = workspace ? await profiles.FindAsync(conversationId, ct) : null;
        if (workspace && profile is not { Archived: false }) throw new ArgumentException("Projet indisponible.");
        var owned = workspace ? database.Attachments.Where(f => f.WorkspaceId == conversationId)
            : database.Attachments.Where(f => f.ConversationId == conversationId);
        var previous = await owned.AsNoTracking()
            .Where(f => f.BatchId == batchId).ToListAsync(ct);
        var payloads = await batch.PrepareAsync(conversationId, workspace, (conversation?.MessageCount ?? 0) + 1, ct);
        var files = payloads.Select(p => p.File).ToArray();
        if (previous.Count > 0)
        {
            var oldRoots = previous.Where(f => f.ArchiveId == null).Select(f => (f.Name, f.Sha256)).Order().ToArray();
            var newRoots = files.Where(f => f.ArchiveId == null).Select(f => (f.Name, f.Sha256)).Order().ToArray();
            if (!oldRoots.SequenceEqual(newRoots)) throw new ChatConflictException();
            return previous.OrderBy(f => f.CreatedAtUtc).Select(Summary).ToArray();
        }
        var stored = await owned
            .Select(f => f.Size).ToListAsync(ct);
        if (limits.StoredFiles > 0 && stored.Count + files.Length > limits.StoredFiles
            || limits.StoredMiB > 0 && stored.Sum() + payloads.Sum(p => p.File.Size) > limits.StoredBytes)
            throw new ArgumentException($"Quota de stockage atteint ({limits.StoredFiles} fichiers / {limits.StoredMiB} Mio ; 0 signifie sans quota). Modifiez Imports et stockage dans les paramètres.");
        Directory.CreateDirectory(workspace ? storage.WorkspaceDirectory(conversationId) : storage.DirectoryFor(conversationId));
        var written = new List<string>();
        var committing = false;
        try
        {
            foreach (var (file, bytes) in payloads)
            {
                var path = storage.PathFor(file);
                written.Add(path);
                await File.WriteAllBytesAsync(path, bytes, ct);
            }
            database.Attachments.AddRange(files);
            if (conversation is not null) await conversations.StageAdvanceAsync(conversationId, conversation.Revision, ct);
            if (profile is null)
            {
                committing = true;
                await database.SaveVersionedAsync(ct);
            }
            else
            {
                await using var transaction = await database.Database.BeginTransactionAsync(ct);
                await profiles.AdvanceAsync(conversationId, profile.Revision, ct);
                await database.SaveVersionedAsync(ct);
                committing = true;
                await transaction.CommitAsync(ct);
            }
        }
        catch (ChatConflictException)
        {
            // SaveChanges rolled back the losing transaction. Each attempt uses fresh file IDs.
            foreach (var path in written) File.Delete(path);
            throw;
        }
        catch
        {
            // On ambiguous DB commit failure keep bytes: never delete a file that
            // might already be referenced. Orphans can be reconciled offline.
            if (!committing) foreach (var path in written) File.Delete(path);
            throw;
        }
        return files.Select(Summary).ToArray();
    }

    public static string NormalizeName(string name) => DocumentContent.NormalizeName(name);
    public static Task<byte[]> ReadBoundedAsync(Stream input, int maximum, CancellationToken ct) =>
        DocumentContent.ReadBoundedAsync(input, maximum, ct);
    public static (string Text, string Status) Extract(string name, byte[] bytes) => DocumentContent.Extract(name, bytes);
}
