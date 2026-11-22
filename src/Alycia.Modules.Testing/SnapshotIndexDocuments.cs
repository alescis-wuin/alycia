namespace Alycia.Modules.Testing;

public sealed record IndexDocumentFixture(Guid Owner, bool Project, IndexSourceFile File, IndexedOriginal? Original,
    SourceChunk[] Flat, SourceChunk[] Hierarchical, bool ExcludedAtSource = false, bool HasIndexedText = true);

public sealed class SnapshotIndexDocuments(IEnumerable<IndexDocumentFixture> documents) : IIndexSourceDocuments
{
    private readonly IndexDocumentFixture[] _documents = documents.Select(d => d with
    {
        Flat = Copy(d.Flat), Hierarchical = Copy(d.Hierarchical)
    }).ToArray();

    private static SourceChunk[] Copy(IEnumerable<SourceChunk> chunks) =>
        chunks.Select(c => c with { References = c.References.ToArray() }).ToArray();

    private IEnumerable<IndexDocumentFixture> Visible(Guid owner, bool project) =>
        _documents.Where(d => d.Owner == owner && d.Project == project && d.File.ContextMode != "excluded");

    public Task<IndexSourceFile[]> PageAsync(Guid owner, bool project, Guid[]? allowed, IndexFileCursor? after,
        bool orderByName, int count, CancellationToken ct)
    {
        if (count is < 1 or > 128) throw new ArgumentOutOfRangeException(nameof(count));
        ct.ThrowIfCancellationRequested();
        var files = Visible(owner, project).Where(d => d.HasIndexedText).Select(d => d.File);
        if (allowed is not null) files = files.Where(f => allowed.Contains(f.Id));
        if (after is not null)
            files = files.Where(f => orderByName
                ? string.CompareOrdinal(f.Name, after.Name) > 0 || f.Name == after.Name && f.Id.CompareTo(after.Id) > 0
                : f.Id.CompareTo(after.Id) > 0);
        var ordered = orderByName ? files.OrderBy(f => f.Name, StringComparer.Ordinal).ThenBy(f => f.Id) : files.OrderBy(f => f.Id);
        return Task.FromResult(ordered.Take(count).ToArray());
    }

    public Task<IndexSourceFile?> FindAsync(Guid owner, bool project, Guid file, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(Visible(owner, project).SingleOrDefault(d => d.File.Id == file)?.File);
    }

    public Task<IndexedOriginal> ReadAsync(Guid owner, bool project, Guid file, string sha256, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var entry = Visible(owner, project).SingleOrDefault(d => d.File.Id == file && d.File.Sha256 == sha256);
        return Task.FromResult(entry?.Original ?? throw new ArgumentException("Cette source est indisponible."));
    }

    public IReadOnlyList<SourceChunk> Split(IndexSourceFile file, IndexedOriginal original, bool hierarchical, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var entry = _documents.FirstOrDefault(d => d.File.Id == file.Id && d.File.Sha256 == file.Sha256 && d.Original == original)
            ?? throw new ArgumentException("Segmentation absente du jeu d’essai.");
        return Copy(hierarchical ? entry.Hierarchical : entry.Flat);
    }

    public Task<Guid[]> ExcludedPageAsync(Guid? after, int count, CancellationToken ct)
    {
        if (count is < 1 or > 128) throw new ArgumentOutOfRangeException(nameof(count));
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(_documents.Where(d => d.ExcludedAtSource).Select(d => d.File.Id).Distinct()
            .Where(id => after is null || id.CompareTo(after.Value) > 0).Order().Take(count).ToArray());
    }
}
