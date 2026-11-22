namespace Alycia.Contracts.Documents;

public sealed record DownloadedOriginal(Stream Content, string Name, DateTimeOffset LastModified);
public sealed class OriginalUnavailableException(Exception cause) : IOException("Le volume de fichiers est indisponible.", cause);

public interface IConversationOriginals
{
    Task<DownloadedOriginal?> DownloadAsync(Guid conversation, Guid file, CancellationToken ct);
}

public interface IArchiveDocuments
{
    Task<string[]> DescribeWorkspaceAsync(Guid workspace, CancellationToken ct);
    IDocumentFork Fork(Guid source, Guid destination, int through, bool copyOverrides);
}

public interface IDocumentFork
{
    Task PrepareAsync(CancellationToken ct);
    void Detach(bool discardOriginals);
}
