namespace Alycia.Contracts.Indexing;

public sealed record IndexSourceFile(Guid Id, string Name, string Sha256, string Status, string ContextMode,
    DateTimeOffset CreatedAtUtc, string SourceCreatedAt, string SourceModifiedAt, string MetadataSource);
public sealed record IndexedOriginal(string Text, bool Complete, string Origin)
{
    // Stored passage keys, checkpoints and caches include this segmentation format.
    public const int StructureVersion = 3;
}
public sealed record IndexFileCursor(Guid Id, string Name);

public interface IIndexSourceDocuments
{
    Task<IndexSourceFile[]> PageAsync(Guid owner, bool project, Guid[]? allowed, IndexFileCursor? after,
        bool orderByName, int count, CancellationToken ct);
    Task<IndexSourceFile?> FindAsync(Guid owner, bool project, Guid file, CancellationToken ct);
    Task<IndexedOriginal> ReadAsync(Guid owner, bool project, Guid file, string sha256, CancellationToken ct);
    IReadOnlyList<SourceChunk> Split(IndexSourceFile file, IndexedOriginal original, bool hierarchical, CancellationToken ct);
    Task<Guid[]> ExcludedPageAsync(Guid? after, int count, CancellationToken ct);
}
