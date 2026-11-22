namespace Alycia.Contracts.Documents;

public sealed record TurnFile(Guid Id,
    Guid? WorkspaceId,
    Guid? ConversationId,
    Guid BatchId,
    Guid? ArchiveId,
    string Name,
    long Size,
    string Sha256,
    string Status,
    string Text,
    int AnchorSequence,
    DateTimeOffset CreatedAtUtc,
    string SourceCreatedAt,
    string SourceModifiedAt,
    string MetadataSource,
    string ContextMode)
{
    public bool InContextWindow(int first, int last) => Text.Length > 0 && ContextMode != "excluded"
        && AnchorSequence <= last && (ContextMode is "pinned" or "project" || AnchorSequence >= first);
}

public interface ITurnDocuments
{
    Task<IReadOnlyList<TurnFile>> FilesAsync(Guid owner, CancellationToken ct);
    Task<IReadOnlyList<TurnFile>> ContextFilesAsync(Guid owner, int first, CancellationToken ct);
}
