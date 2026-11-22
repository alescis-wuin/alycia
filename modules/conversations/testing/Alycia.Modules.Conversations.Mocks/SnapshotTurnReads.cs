using System.Collections.Frozen;
using System.Collections.Immutable;
using Alycia.Contracts.Context;
using Alycia.Contracts.Documents;
using Alycia.Contracts.Tools;

namespace Alycia.Modules.Testing;

public sealed class SnapshotTurnReads(IEnumerable<Guid> owners, IEnumerable<HistoryMessage> history,
    ITurnDocuments documents)
    : IConversationContextReader, IQuerySources, IToolHistoryReader
{
    private readonly FrozenSet<Guid> _owners = owners.ToFrozenSet();
    private readonly ILookup<Guid, HistoryMessage> _history = history.OrderBy(m => m.Sequence).ToLookup(m => m.ConversationId);
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

    public Task<IReadOnlyList<TurnFile>> ContextFilesAsync(Guid id, int first, CancellationToken ct) =>
        documents.ContextFilesAsync(id, first, ct);

    private static Task<IReadOnlyList<T>> Result<T>(IEnumerable<T> values) =>
        Task.FromResult<IReadOnlyList<T>>(values.ToImmutableArray());
}
