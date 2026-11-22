namespace Alycia.Modules.Testing;

// Deterministic lexical scores for controlled corpora. Rank consumes the caller's scores.
public sealed class FixturePassageScoring : IPassageScoring
{
    public double[] Bm25(IReadOnlyList<SourceChunk> chunks, string query)
    {
        var terms = LexicalTerms.Split(query).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return chunks.Select(c => (double)LexicalTerms.Split(c.Path + " " + c.Symbol + " " + c.Text).Count(terms.Contains)).ToArray();
    }

    public IReadOnlyList<SourceChunk> Rank(IReadOnlyList<SourceChunk> chunks, string query, double[] scores)
    {
        if (scores.Length != chunks.Count) throw new ArgumentException("Dimensions lexicales invalides.");
        return chunks.Select((c, n) => (Chunk: c, Score: scores[n])).Where(e => e.Score > 0)
            .OrderByDescending(e => e.Score).ThenBy(e => e.Chunk.Path, StringComparer.Ordinal).ThenBy(e => e.Chunk.Start)
            .Select(e => e.Chunk).ToArray();
    }
}
