using Alycia.Contracts.Memory;

namespace Alycia.Modules.Testing;

public sealed class MemoryTemporalFacts(IEnumerable<Guid> existingOwners) : ITemporalFactStorage
{
    private sealed record Entry(Guid Owner, int Sequence, DateTimeOffset Observed, string[] Topics, string Facts, string Search);
    private readonly HashSet<Guid> _owners = existingOwners.ToHashSet();
    private readonly Dictionary<(Guid, int), Entry> _rows = [];

    public Task SaveAsync(Guid owner, int sequence, DateTimeOffset observed, string[] topics, string facts, string search, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        lock (_rows)
            if (_owners.Contains(owner)) _rows.TryAdd((owner, sequence), new(owner, sequence, observed, topics.ToArray(), facts, search));
        return Task.CompletedTask;
    }
    private Entry[] Rows(Guid owner, int through, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        lock (_rows) return _rows.Values.Where(r => r.Owner == owner && r.Sequence <= through).OrderBy(r => r.Sequence).ToArray();
    }
    public Task<int> ContiguousThroughAsync(Guid owner, int through, CancellationToken ct)
    {
        var rows = Rows(owner, through, ct);
        return Task.FromResult(rows.Length > 0 && rows[^1].Sequence == rows.Length ? rows.Length : 0);
    }
    public Task<int[]> IndexedAsync(Guid owner, int[] sequences, CancellationToken ct) =>
        Task.FromResult(Rows(owner, int.MaxValue, ct).Select(r => r.Sequence).Where(sequences.Contains).ToArray());
    public Task<int> CountAsync(Guid owner, int through, CancellationToken ct) => Task.FromResult(Rows(owner, through, ct).Length);
    public Task<List<string>> LexicalAsync(Guid owner, int through, DateTimeOffset before, string query, CancellationToken ct)
    {
        var terms = LexicalTerms.Split(query).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return Task.FromResult(Rows(owner, through, ct).Where(r => r.Observed < before)
            .OrderByDescending(r => LexicalTerms.Split(r.Search).Count(terms.Contains)).ThenByDescending(r => r.Sequence).Take(96)
            .Select(r => r.Facts).ToList());
    }
    public Task<List<string>> SelectedAsync(Guid owner, int through, DateTimeOffset before, int[] sequences, CancellationToken ct) =>
        Task.FromResult(Rows(owner, through, ct).Where(r => r.Observed < before && sequences.Contains(r.Sequence)).Select(r => r.Facts).ToList());
    public Task<List<string>> TopicsAsync(Guid owner, int through, DateTimeOffset before, string[] topics, CancellationToken ct) =>
        Task.FromResult(Rows(owner, through, ct).Where(r => r.Observed < before && topics.Intersect(r.Topics).Any()).Take(257).Select(r => r.Facts).ToList());
    public Task<List<string>> ExportAsync(Guid owner, int after, int limit, CancellationToken ct) =>
        Task.FromResult(Rows(owner, int.MaxValue, ct).Where(r => r.Sequence > after).Take(limit).Select(r => r.Facts).ToList());
    public Task<int> ClearAsync(Guid owner, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        lock (_rows)
        {
            var keys = _rows.Keys.Where(k => k.Item1 == owner).ToArray();
            foreach (var key in keys) _rows.Remove(key);
            return Task.FromResult(keys.Length);
        }
    }
}
