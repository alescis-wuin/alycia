using System.Text.RegularExpressions;

namespace Alycia.Contracts.Indexing;

public static class LexicalTerms
{
    private static readonly Regex Terms = new(@"[\p{L}\p{N}_]+", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2));
    private static readonly Regex Camel = new(@"(?<=[a-z])(?=[A-Z])", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2));
    private static readonly HashSet<string> Stop = new("le la les un une des du de en et ou à au aux ce cette ces qui que quoi quel quelle quels quelles comment pourquoi dans pour par sur avec est sont soit pas plus explique expliquer code fichier fonction méthode projet".Split(' '));
    public static string[] Split(string text) => Terms.Matches(Camel.Replace(text, " ").ToLowerInvariant()).Select(m => m.Value)
        .Where(t => t.Length > 1 && !Stop.Contains(t)).ToArray();
}
