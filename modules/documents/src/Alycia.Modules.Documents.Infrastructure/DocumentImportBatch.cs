using System.Security.Cryptography;

namespace Alycia.Modules.Documents.Infrastructure;

public readonly record struct PreparedDocument(Attachment File, byte[] Bytes);

public sealed class DocumentImportBatch
{
    private readonly Guid _batchId;
    private readonly IReadOnlyList<IncomingFile> _incoming;
    private readonly ImportLimits _limits;

    public DocumentImportBatch(Guid batchId, IReadOnlyList<IncomingFile> incoming, ImportLimits limits)
    {
        if (batchId == Guid.Empty || incoming.Count < 1 || incoming.Count > limits.BatchEntries)
            throw new ArgumentException($"Import limité à {limits.BatchEntries} fichiers ; identifiant de lot requis.");
        _batchId = batchId;
        _incoming = incoming.ToArray();
        _limits = limits;
    }

    public async Task<IReadOnlyList<PreparedDocument>> PrepareAsync(Guid ownerId, bool workspace, int anchorSequence, CancellationToken ct)
    {
        var documents = new List<PreparedDocument>();
        var names = new HashSet<string>(StringComparer.Ordinal);
        long total = 0;

        async Task AddAsync(IncomingFile incoming, Guid? archive, int depth)
        {
            ct.ThrowIfCancellationRequested();
            if (documents.Count >= _limits.BatchEntries)
                throw new ArgumentException($"Archive ou lot limité à {_limits.BatchEntries} entrées, original compris.");
            var name = DocumentContent.NormalizeName(incoming.Name);
            if (!names.Add(name)) throw new ArgumentException("Le lot contient deux chemins identiques : " + name);
            if (incoming.SourceCreatedAt.Length > 64 || incoming.SourceModifiedAt.Length > 64 || incoming.MetadataSource.Length > 100)
                throw new ArgumentException("Métadonnées de source invalides.");

            var bytes = await DocumentContent.ReadBoundedAsync(incoming.Content, _limits.FileBytes, ct);
            total += bytes.Length;
            if (total > _limits.BatchBytes) throw new ArgumentException($"Lot décompressé limité à {_limits.BatchMiB} Mio.");
            var file = new Attachment
            {
                Id = Guid.NewGuid(),
                ConversationId = workspace ? null : ownerId,
                WorkspaceId = workspace ? ownerId : null,
                BatchId = _batchId,
                ArchiveId = archive,
                ContextMode = workspace || archive.HasValue || name.Contains('/') ? "project" : "window",
                Name = name,
                Size = bytes.Length,
                Sha256 = Convert.ToHexString(SHA256.HashData(bytes)),
                AnchorSequence = anchorSequence,
                SourceCreatedAt = incoming.SourceCreatedAt,
                SourceModifiedAt = incoming.SourceModifiedAt,
                MetadataSource = incoming.MetadataSource,
                CreatedAtUtc = DateTimeOffset.UtcNow
            };
            var isArchive = ArchiveContents.Supports(name);
            (file.Text, file.Status) = isArchive ? ("", "Archive importée récursivement") : DocumentContent.Extract(name, bytes);
            if (ImportPolicy.Excluded(name))
            {
                file.Text = "";
                file.ContextMode = "excluded";
                file.Status = "Fichier technique ou sensible exclu du contexte";
            }
            documents.Add(new(file, bytes));
            if (!isArchive) return;
            if (depth >= _limits.ArchiveDepth)
                throw new ArgumentException($"Archives limitées à {_limits.ArchiveDepth} niveaux imbriqués. Aucun fichier du lot enregistré.");
            await ArchiveContents.ReadAsync(name, bytes, _limits, child => AddAsync(child, file.Id, depth + 1), ct);
        }

        foreach (var source in _incoming) await AddAsync(source, null, 0);
        return documents;
    }
}
