namespace Alycia.Modules.Testing;

public sealed record ScopedVector(Guid Owner, string Identity, string Key, float[] Coordinates,
    int? Sequence = null, DateTimeOffset? Observed = null);

public sealed class SnapshotSimilarities(IEnumerable<ScopedVector> values) : IEmbeddingSimilarityReader
{
    private readonly ScopedVector[] _values = values.Select(v => v with { Coordinates = v.Coordinates.ToArray() }).ToArray();

    public Task<Dictionary<string, double>> SimilaritiesAsync(Guid owner, string identity, string[] keys, float[] query, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var selected = keys.ToHashSet(StringComparer.Ordinal);
        return Task.FromResult(_values.Where(v => v.Owner == owner && v.Identity == identity && selected.Contains(v.Key)
            && v.Coordinates.Length == query.Length).ToDictionary(v => v.Key, v => VectorScores.Cosine(query, v.Coordinates)));
    }

    public Task<Dictionary<int, double>> MemorySimilaritiesAsync(Guid owner, string identity, int through, DateTimeOffset before,
        float[] query, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var rows = _values.Where(v => v.Owner == owner && v.Identity == identity && v.Observed < before
            && v.Sequence <= through && v.Coordinates.Length == query.Length);
        return Task.FromResult(rows.GroupBy(v => v.Sequence!.Value).Select(g => (Sequence: g.Key, Score: g.Max(v => VectorScores.Cosine(query, v.Coordinates))))
            .OrderByDescending(e => e.Score).ThenByDescending(e => e.Sequence).Take(32).ToDictionary(e => e.Sequence, e => e.Score));
    }
}
