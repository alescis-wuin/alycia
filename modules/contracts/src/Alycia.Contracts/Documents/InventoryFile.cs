namespace Alycia.Contracts.Documents;

public sealed record InventoryFile(Guid Id, Guid BatchId, Guid? ArchiveId, string Path, long Bytes,
    string Sha256, string Extension, string SourceCreatedAt, string SourceModifiedAt, string MetadataSource,
    DateTimeOffset ImportedAtUtc, string ExtractionStatus, string ContextMode);
