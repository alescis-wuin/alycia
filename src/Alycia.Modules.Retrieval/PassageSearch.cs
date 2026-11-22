using System.Security.Cryptography;
using System.Text;

namespace Alycia.Modules.Retrieval;

public static class PassageSearch
{
    internal static string Fingerprint(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    public static string[] Tokens(string text) => LexicalTerms.Split(text);
    public static string Reference(SourceChunk c) => Reference(c.FileId, c.Sha256, c.Start, c.End);
    public static string Reference(Guid fileId, string sha, int start, int end) => SourceCoordinates.Reference(fileId, sha, start, end);


    public static double[] Bm25(IReadOnlyList<SourceChunk> chunks, string query) => new LexicalIndex(chunks).Score(query);

    public static IReadOnlyList<SourceChunk> Rank(IReadOnlyList<SourceChunk> chunks, string query, double[]? similarities = null, double[]? lexicalScores = null)
    {
        var lexical = lexicalScores ?? Bm25(chunks, query);
        if (lexical.Length != chunks.Count) throw new ArgumentException("Dimensions lexicales invalides.");
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
        var baseScores = score.Select((s, i) => s + (related.Any(r => chunks[i].Symbol.Contains(r, StringComparison.Ordinal)) ? boost : 0)).ToArray();
        var queues = Enumerable.Range(0, chunks.Count).Where(i => score[i] > 0).GroupBy(i => chunks[i].FileId)
            .ToDictionary(g => g.Key, g => new Queue<int>(g.OrderByDescending(i => baseScores[i])
                .ThenBy(i => chunks[i].Path, StringComparer.Ordinal).ThenBy(i => chunks[i].Start).ThenBy(i => i)));
        var priority = Comparer<(double Score, string Path, int Start, int Index)>.Create((a, b) =>
        {
            var compare = a.Score.CompareTo(b.Score); if (compare != 0) return compare;
            compare = StringComparer.Ordinal.Compare(a.Path, b.Path); if (compare != 0) return compare;
            compare = a.Start.CompareTo(b.Start); return compare != 0 ? compare : a.Index.CompareTo(b.Index);
        });
        var heap = new PriorityQueue<int, (double Score, string Path, int Start, int Index)>(priority);
        var counts = new Dictionary<Guid, int>(); var ordered = new List<SourceChunk>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        void Enqueue(Guid file)
        {
            if (!queues[file].TryDequeue(out var i)) return;
            heap.Enqueue(i, (-baseScores[i] / (1 + .6 * counts.GetValueOrDefault(file)), chunks[i].Path, chunks[i].Start, i));
        }
        foreach (var file in queues.Keys) Enqueue(file);
        while (heap.TryDequeue(out var i, out _))
        {
            if (seen.Add(Fingerprint(chunks[i].Text.Trim())))
            { ordered.Add(chunks[i]); counts[chunks[i].FileId] = counts.GetValueOrDefault(chunks[i].FileId) + 1; }
            Enqueue(chunks[i].FileId);
        }
        return ordered;
    }
}
