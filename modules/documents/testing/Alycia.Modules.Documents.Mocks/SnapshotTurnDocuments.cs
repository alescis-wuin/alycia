using System.Collections.Immutable;
using Alycia.Contracts.Documents;

namespace Alycia.Modules.Testing;

public sealed class SnapshotTurnDocuments(IEnumerable<(Guid Owner, TurnFile File)> scopedFiles) : ITurnDocuments
{
    private readonly ILookup<Guid, TurnFile> _files = scopedFiles.OrderBy(x => x.File.CreatedAtUtc).ThenBy(x => x.File.Id)
        .ToLookup(x => x.Owner, x => x.File);

    public Task<IReadOnlyList<TurnFile>> FilesAsync(Guid id, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Result(_files[id]);
    }

    public Task<IReadOnlyList<TurnFile>> ContextFilesAsync(Guid id, int first, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Result(_files[id].Where(f => f.InContextWindow(first, int.MaxValue))
            .OrderByDescending(f => f.ContextMode == "pinned").DistinctBy(f => (f.Name, f.Sha256)));
    }

    private static Task<IReadOnlyList<T>> Result<T>(IEnumerable<T> values) =>
        Task.FromResult<IReadOnlyList<T>>(values.ToImmutableArray());
}
