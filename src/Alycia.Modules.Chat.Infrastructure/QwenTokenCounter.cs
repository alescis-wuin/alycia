using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Alycia.Modules.Chat.Application;

namespace Alycia.Modules.Chat.Infrastructure;

// Qwen2 pre-tokenizer + byte BPE, read from the installed model's GGUF metadata.
// Unsupported metadata/astral pre-tokenization falls back to UTF-8 rather than guessing.
public sealed class QwenTokenCounter : ITextTokenCounter
{
    public string Name => "Qwen2 BPE du modèle + marge de format ; repli UTF-8 pour Unicode astral";
    private static readonly Regex Pieces = new(@"(?i:'s|'t|'re|'ve|'m|'ll|'d)|[^\r\n\p{L}\p{N}]?\p{L}+|\p{N}| ?[^\s\p{L}\p{N}]+[\r\n]*|\s*[\r\n]+|\s+(?!\S)|\s+", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2));
    private static readonly char[] ByteChars = BuildByteChars();
    private readonly Dictionary<(string, string), int> _ranks;
    private QwenTokenCounter(Dictionary<(string, string), int> ranks) => _ranks = ranks;
    public static QwenTokenCounter? FromMetadata(JsonElement info)
    {
        if (!info.TryGetProperty("tokenizer.ggml.pre", out var pre) || pre.GetString() != "qwen2"
            || !info.TryGetProperty("tokenizer.ggml.model", out var type) || type.GetString() != "gpt2"
            || !info.TryGetProperty("tokenizer.ggml.tokens", out var tokens) || tokens.ValueKind != JsonValueKind.Array
            || !info.TryGetProperty("tokenizer.ggml.merges", out var merges) || merges.ValueKind != JsonValueKind.Array
            || tokens.GetArrayLength() > 300000 || merges.GetArrayLength() > 500000) return null;
        var vocabulary = tokens.EnumerateArray().Select(t => t.GetString()!).ToHashSet(StringComparer.Ordinal);
        if (!ByteChars.All(c => vocabulary.Contains(c.ToString()))) return null;
        var ranks = new Dictionary<(string, string), int>();
        foreach (var merge in merges.EnumerateArray())
        {
            var pair = merge.GetString()!.Split(' ');
            if (pair.Length != 2 || !vocabulary.Contains(pair[0] + pair[1]) || !ranks.TryAdd((pair[0], pair[1]), ranks.Count)) return null;
        }
        return ranks.Count == 0 ? null : new(ranks);
    }
    public int Count(string text)
    {
        if (text.Any(char.IsSurrogate)) return Encoding.UTF8.GetByteCount(text);
        try
        {
            var total = 0;
            foreach (Match piece in Pieces.Matches(text))
            {
                var data = Encoding.UTF8.GetBytes(piece.Value);
                if (data.Length > 8192) { total += data.Length; continue; }
                var nodes = data.Select(b => ByteChars[b].ToString()).ToArray();
                var next = Enumerable.Range(1, nodes.Length).ToArray();
                var prev = Enumerable.Range(-1, nodes.Length).ToArray();
                var active = Enumerable.Repeat(true, nodes.Length).ToArray();
                var versions = new int[nodes.Length];
                var queue = new PriorityQueue<(int Left, int Version), (int Rank, int Index)>();
                void Enqueue(int left)
                {
                    if (left >= 0 && active[left] && next[left] < nodes.Length && _ranks.TryGetValue((nodes[left], nodes[next[left]]), out var rank))
                        queue.Enqueue((left, versions[left]), (rank, left));
                }
                for (var i = 0; i < nodes.Length - 1; i++) Enqueue(i);
                var count = nodes.Length;
                while (queue.TryDequeue(out var candidate, out _))
                {
                    var left = candidate.Left;
                    if (!active[left] || versions[left] != candidate.Version || next[left] >= nodes.Length) continue;
                    var right = next[left];
                    nodes[left] += nodes[right]; active[right] = false; next[left] = next[right]; versions[left]++; count--;
                    if (next[left] < nodes.Length) prev[next[left]] = left;
                    if (prev[left] >= 0) { versions[prev[left]]++; Enqueue(prev[left]); }
                    Enqueue(left);
                }
                total += count;
            }
            return total;
        }
        catch (RegexMatchTimeoutException) { return Encoding.UTF8.GetByteCount(text); }
    }
    private static char[] BuildByteChars()
    {
        var chars = new char[256]; var extra = 256;
        for (var i = 0; i < 256; i++) chars[i] = (char)(i is >= 33 and <= 126 or >= 161 and <= 172 or >= 174 ? i : extra++);
        return chars;
    }
}
