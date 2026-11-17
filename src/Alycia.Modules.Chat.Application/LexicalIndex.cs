namespace Alycia.Modules.Chat.Application;

public sealed class LexicalIndex
{
    private readonly Dictionary<string, int>[] _documents;
    private readonly Dictionary<string, int> _frequencies;
    private readonly int[] _lengths;
    private readonly double _average;
    public LexicalIndex(IReadOnlyList<SourceChunk> chunks)
    {
        _documents = chunks.Select(c => ProjectRetrieval.Tokens(c.Text + " " + c.Path + " " + c.Symbol + " " + c.Symbol + " " + c.Symbol)
            .GroupBy(t => t).ToDictionary(g => g.Key, g => g.Count())).ToArray();
        _lengths = _documents.Select(d => d.Values.Sum()).ToArray();
        _average = Math.Max(1, _lengths.Length == 0 ? 1 : _lengths.Average());
        _frequencies = _documents.SelectMany(d => d.Keys).GroupBy(t => t).ToDictionary(g => g.Key, g => g.Count());
    }
    public double[] Score(string query)
    {
        var scores = new double[_documents.Length];
        foreach (var term in ProjectRetrieval.Tokens(query).Distinct().Take(32))
        {
            var frequency = _frequencies.GetValueOrDefault(term);
            var idf = Math.Log(1 + (_documents.Length - frequency + .5) / (frequency + .5));
            for (var i = 0; i < _documents.Length; i++)
                if (_documents[i].TryGetValue(term, out var count)) scores[i] += idf * count * 2.2 / (count + 1.2 * (.25 + .75 * _lengths[i] / _average));
        }
        return scores;
    }
}

// At most four conversations and eight million source characters; entries never cross scopes.
public sealed class LexicalIndexCache
{
    private sealed record Entry(string Fingerprint, LexicalIndex Index, long Characters, long Used);
    private readonly Dictionary<Guid, Entry> _entries = [];
    private readonly object _gate = new();
    private long _clock;
    public (LexicalIndex Index, bool Hit) Get(Guid conversation, IReadOnlyList<SourceChunk> chunks)
    {
        var fingerprint = StructuredMemory.Hash("lexical-v1\n" + string.Join('\n', chunks.Select(c =>
            $"{c.FileId}:{c.Sha256}:{c.Start}:{c.End}:{c.Path}:{c.Symbol}:{StructuredMemory.Hash(c.Text)}")));
        lock (_gate)
        {
            if (_entries.TryGetValue(conversation, out var entry) && entry.Fingerprint == fingerprint)
            { _entries[conversation] = entry with { Used = ++_clock }; return (entry.Index, true); }
        }
        var created = new LexicalIndex(chunks);
        var characters = chunks.Sum(c => (long)c.Text.Length + c.Path.Length + c.Symbol.Length * 3);
        lock (_gate)
        {
            _entries.Remove(conversation);
            if (characters <= 8_000_000)
            {
                while (_entries.Count >= 4 || _entries.Values.Sum(e => e.Characters) + characters > 8_000_000)
                    _entries.Remove(_entries.MinBy(p => p.Value.Used).Key);
                _entries[conversation] = new(fingerprint, created, characters, ++_clock);
            }
        }
        return (created, false);
    }
}
