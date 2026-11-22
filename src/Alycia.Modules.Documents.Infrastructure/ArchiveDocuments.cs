using Microsoft.EntityFrameworkCore;

namespace Alycia.Modules.Documents.Infrastructure;

public sealed class ArchiveDocuments(DbContext database, FileStorage storage) : IArchiveDocuments
{
    public Task<string[]> DescribeWorkspaceAsync(Guid workspace, CancellationToken ct) =>
        database.Set<Attachment>().AsNoTracking().Where(f => f.WorkspaceId == workspace)
            .OrderBy(f => f.Name).Select(f => f.Name + " · " + f.Sha256).ToArrayAsync(ct);

    public IDocumentFork Fork(Guid source, Guid destination, int through, bool copyOverrides) =>
        new ForkAttempt(database, storage, source, destination, through, copyOverrides);

    private sealed class ForkAttempt(DbContext database, FileStorage storage, Guid sourceId,
        Guid copyId, int through, bool copyOverrides) : IDocumentFork
    {
        private readonly List<string> written = [];
        private readonly List<Attachment> copies = [];
        private readonly List<WorkspaceFileOverride> overrides = [];
        private string? directory;

        public async Task PrepareAsync(CancellationToken ct)
        {
            var files = await database.Set<Attachment>().AsNoTracking().Where(f => f.ConversationId == sourceId
                && f.AnchorSequence <= through + 1).ToListAsync(ct);
            if (files.Count > 0)
            {
                var path = storage.DirectoryFor(copyId);
                if (Directory.Exists(path)) throw new IOException("Le répertoire de la copie existe déjà.");
                Directory.CreateDirectory(path);
                directory = path;
            }
            copies.AddRange(await CopyAttachmentsAsync(files, ct));
            if (copyOverrides)
            {
                var modes = await database.Set<WorkspaceFileOverride>().AsNoTracking().Where(o => o.ConversationId == sourceId).ToArrayAsync(ct);
                overrides.AddRange(modes.Select(o => new WorkspaceFileOverride
                { ConversationId = copyId, FileId = o.FileId, Mode = o.Mode }));
            }
            database.Set<Attachment>().AddRange(copies);
            database.Set<WorkspaceFileOverride>().AddRange(overrides);
        }

        public void Detach(bool discardOriginals)
        {
            foreach (var file in copies) database.Entry(file).State = EntityState.Detached;
            foreach (var mode in overrides) database.Entry(mode).State = EntityState.Detached;
            if (!discardOriginals || directory is null) return;
            foreach (var path in written) File.Delete(path);
            if (!Directory.EnumerateFileSystemEntries(directory).Any()) Directory.Delete(directory);
        }

        private async Task<List<Attachment>> CopyAttachmentsAsync(IReadOnlyList<Attachment> files, CancellationToken ct)
        {
            var ids = files.ToDictionary(f => f.Id, _ => Guid.NewGuid());
            var copies = new List<Attachment>();
            foreach (var file in files)
            {
                var newId = ids[file.Id];
                await using var source = File.OpenRead(storage.PathFor(sourceId, file.Id));
                var path = storage.PathFor(copyId, newId);
                await using var target = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                written.Add(path);
                await VerifiedOriginal.CopyAsync(source, target, file.Size, file.Sha256, ct);
                copies.Add(new Attachment
                {
                    Id = newId,
                    ConversationId = copyId,
                    BatchId = copyId,
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
            return copies;
        }
    }
}
