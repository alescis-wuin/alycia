using Alycia.Modules.Chat.Application;

// Frozen references for equivalence, not production implementations.
// Bm25: 0b31f13; Rank: cdb5fee (phase 04).
internal static class ReferenceAlgorithms
{
    public static double[] Bm25(IReadOnlyList<SourceChunk> chunks, string query)
    {
        var terms = LexicalTerms.Split(query).Distinct().Take(32).ToArray();
        var documents = chunks.Select(c => LexicalTerms.Split(c.Text + " " + c.Path + " " + c.Symbol + " " + c.Symbol + " " + c.Symbol)
            .GroupBy(t => t).ToDictionary(g => g.Key, g => g.Count())).ToArray();
        var lengths = documents.Select(d => d.Values.Sum()).ToArray();
        var average = Math.Max(1, lengths.Length == 0 ? 1 : lengths.Average());
        var scores = new double[chunks.Count];
        foreach (var term in terms)
        {
            var frequency = documents.Count(d => d.ContainsKey(term));
            var idf = Math.Log(1 + (chunks.Count - frequency + .5) / (frequency + .5));
            for (var i = 0; i < chunks.Count; i++)
                if (documents[i].TryGetValue(term, out var count)) scores[i] += idf * count * 2.2 / (count + 1.2 * (.25 + .75 * lengths[i] / average));
        }
        return scores;
    }

    public static IReadOnlyList<SourceChunk> Rank(IReadOnlyList<SourceChunk> chunks, string query, double[]? similarities = null)
    {
        var lexical = Bm25(chunks, query);
        var score = lexical.ToArray();
        if (similarities is not null)
        {
            if (similarities.Length != chunks.Count) throw new ArgumentException("Dimensions de recherche invalides.");
            score = new double[chunks.Count];
            foreach (var ranking in new[] { lexical, similarities })
            {
                var order = Enumerable.Range(0, chunks.Count).Where(i => ranking[i] > 0).OrderByDescending(i => ranking[i]).ThenBy(i => i).ToArray();
                for (var rank = 0; rank < order.Length; rank++) score[order[rank]] += 1.0 / (60 + rank + 1);
            }
        }
        var seeds = Enumerable.Range(0, chunks.Count).OrderByDescending(i => score[i]).Take(3).Where(i => score[i] > 0);
        var related = seeds.SelectMany(i => chunks[i].References).Where(s => s.Length >= 4).ToHashSet(StringComparer.Ordinal);
        // Small structural tie-breaker, not a learned reranker or a semantic call graph.
        var boost = similarities is null ? .15 : .001;
        var remaining = Enumerable.Range(0, chunks.Count).Where(i => score[i] > 0).ToList();
        var ordered = new List<SourceChunk>();
        var counts = new Dictionary<Guid, int>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        while (remaining.Count > 0)
        {
            var i = remaining.OrderByDescending(i => (score[i]
                + (related.Any(r => chunks[i].Symbol.Contains(r, StringComparison.Ordinal)) ? boost : 0))
                / (1 + .6 * counts.GetValueOrDefault(chunks[i].FileId)))
                .ThenBy(i => chunks[i].Path, StringComparer.Ordinal).ThenBy(i => chunks[i].Start).First();
            remaining.Remove(i);
            if (!seen.Add(StructuredMemory.Hash(chunks[i].Text.Trim()))) continue;
            ordered.Add(chunks[i]); counts[chunks[i].FileId] = counts.GetValueOrDefault(chunks[i].FileId) + 1;
        }
        return ordered;
    }

}
