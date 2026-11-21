using System.Text;

namespace Alycia.Contracts.Documents;

public static class Utf8Text
{
    public static string Prefix(string text, int bytes)
    {
        var result = new StringBuilder();
        foreach (var rune in text.EnumerateRunes())
        {
            if (rune.Utf8SequenceLength > bytes) break;
            result.Append(rune.ToString());
            bytes -= rune.Utf8SequenceLength;
        }
        return result.ToString();
    }

    public static IEnumerable<string> Chunks(string text, int maxBytes)
    {
        if (maxBytes < 4) throw new ArgumentOutOfRangeException(nameof(maxBytes));
        var chunk = new StringBuilder();
        var bytes = 0;
        foreach (var rune in text.EnumerateRunes())
        {
            if (bytes + rune.Utf8SequenceLength > maxBytes)
            {
                yield return chunk.ToString(); chunk.Clear(); bytes = 0;
            }
            chunk.Append(rune.ToString()); bytes += rune.Utf8SequenceLength;
        }
        if (chunk.Length > 0) yield return chunk.ToString();
    }
}
