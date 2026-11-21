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
}
