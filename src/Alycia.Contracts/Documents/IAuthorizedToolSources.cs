using Alycia.Contracts.Indexing;

namespace Alycia.Contracts.Documents;

public sealed record ToolSourceEntry(Guid Id, string Path, long Bytes, string Mode, string Status, bool Readable);
public sealed record AuthorizedToolSource(Guid Id, string Name, string Sha256, string Text, bool Complete, string Origin);

public interface IAuthorizedToolSources
{
    Task<bool> HasFilesAsync(Guid conversation, CancellationToken ct);
    Task<ToolSourceEntry[]> EntriesAsync(Guid conversation, CancellationToken ct);
    Task<AuthorizedToolSource> ReadAsync(Guid conversation, Guid file, CancellationToken ct);
    IReadOnlyList<SourceChunk> Declarations(AuthorizedToolSource file, CancellationToken ct);
}
