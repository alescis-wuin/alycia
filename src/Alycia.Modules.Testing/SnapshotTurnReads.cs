using System.Collections.Frozen;
using System.Collections.Immutable;
using Alycia.Contracts.Context;
using Alycia.Contracts.Documents;
using Alycia.Contracts.Tools;

namespace Alycia.Modules.Testing;

// Files are supplied with the effective scope of each owner, including its local overrides.
public sealed class SnapshotTurnReads(IEnumerable<Guid> owners, IEnumerable<HistoryMessage> history,
    IEnumerable<(Guid Owner, TurnFile File)> scopedFiles)
    : IConversationContextReader, IQuerySources, IToolHistoryReader, ITurnDocuments
{
    private readonly FrozenSet<Guid> _owners = owners.ToFrozenSet();
    private readonly ILookup<Guid, HistoryMessage> _history = history.OrderBy(m => m.Sequence).ToLookup(m => m.ConversationId);
    private readonly ILookup<Guid, TurnFile> _files = scopedFiles.OrderBy(x => x.File.CreatedAtUtc).ThenBy(x => x.File.Id)
        .ToLookup(x => x.Owner, x => x.File);

    public Task<int?> MessageCountAsync(Guid id, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult<int?>(_owners.Contains(id) ? _history[id].Count() : null);
    }

    public Task<IReadOnlyList<HistoryMessage>> RecentAsync(Guid id, int limit, int? before, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Result(_history[id].Where(m => before == null || m.Sequence < before).TakeLast(limit));
    }

    public Task<IReadOnlyList<HistoryMessage>> SummaryRangeAsync(Guid id, int after, int through, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Result(_history[id].Where(m => m.Sequence > after && m.Sequence <= through));
    }

    public async Task<IReadOnlyList<QueryHistoryItem>> RecentForQueryAsync(Guid id, int limit, CancellationToken ct) =>
        (await RecentAsync(id, limit, null, ct)).Select(m => new QueryHistoryItem(id, m.Sequence, m.Role, m.Content)).ToImmutableArray();

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
