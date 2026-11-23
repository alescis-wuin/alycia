using Alycia.Contracts.Context;
using Alycia.Contracts.Memory;

namespace Alycia.Modules.Testing;

public sealed class SnapshotTemporalHistory(IEnumerable<TemporalOwner> eligible, IEnumerable<HistoryMessage> messages) : ITemporalHistorySources
{
    private readonly TemporalOwner[] _owners = eligible.Where(o => o.Through > 0).OrderBy(o => o.Id).ToArray();
    private readonly ILookup<Guid, HistoryMessage> _messages = messages.OrderBy(m => m.Sequence).ToLookup(m => m.ConversationId);

    public Task<TemporalOwner[]> OwnersAsync(Guid? after, int count, CancellationToken ct)
    {
        if (count is < 1 or > 128) throw new ArgumentOutOfRangeException(nameof(count));
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(_owners.Where(o => after == null || o.Id.CompareTo(after.Value) > 0).Take(count).ToArray());
    }

    public Task<int[]> SequencesAsync(Guid owner, int after, int through, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(_messages[owner].Where(m => m.Sequence > after && m.Sequence <= through)
            .Select(m => m.Sequence).Take(256).ToArray());
    }

    public Task<TemporalMessage[]> MessagesAsync(Guid owner, int[] sequences, CancellationToken ct)
    {
        if (sequences.Length > 32) throw new ArgumentOutOfRangeException(nameof(sequences));
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(_messages[owner].Where(m => sequences.Contains(m.Sequence))
            .Select(m => new TemporalMessage(m.Sequence, m.Role, m.Content, m.CreatedAtUtc)).ToArray());
    }
}
