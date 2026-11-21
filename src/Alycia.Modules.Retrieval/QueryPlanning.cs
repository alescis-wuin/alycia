using Alycia.Contracts.Documents;
using System.Text.RegularExpressions;

namespace Alycia.Modules.Retrieval;

public static class QueryPlanning
{
    private static readonly Regex Literals = new("""`([^`]+)`|«([^»]+)»|"([^"]+)"|\b[\w/-]+(?:\.[\w/-]+)+\b|\b[A-Z][a-z]+(?:[A-Z]\w+)+\b""",
        RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(150));
    private static readonly Regex Anaphora = new(@"\b(cette version|ce fichier|cette méthode|celui-ci|celle-ci|et avant|et ensuite|this (?:version|file|method)|what about it|and before)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(150));
    public static SearchQueryPlan Build(string question, IReadOnlyList<QueryHistoryItem> history, IReadOnlyList<TurnFile> sources, int maximum, bool facets = false)
    {
        var known = sources.Where(f => f.ContextMode != "excluded").Select(f => f.Name).ToArray();
        string[] Mentions(string text) => Literals.Matches(text).Select(m => m.Groups.Cast<Group>().Skip(1)
            .FirstOrDefault(g => g.Success)?.Value ?? m.Value).Where(s => s.Length is > 0 and <= 240)
            .Concat(known.Select(Path.GetFileName).OfType<string>().Where(n => n.Length > 0 && text.Contains(n, StringComparison.OrdinalIgnoreCase)))
            .Distinct(StringComparer.OrdinalIgnoreCase).Take(8).ToArray();
        var exact = Mentions(question);
        var refs = new List<ResolvedReferent>();
        var ambiguous = false;
        if (Anaphora.IsMatch(question) && exact.Length == 0)
        {
            ambiguous = true;
            foreach (var turn in history.Where(m => m.Role == "user").OrderByDescending(m => m.Sequence))
            {
                var mentions = Mentions(turn.Content);
                if (mentions.Length == 0) continue;
                if (mentions.Length == 1)
                {
                    var anchor = mentions[0];
                    refs.Add(new(anchor, turn.Sequence, $"H{turn.Sequence}:{MemoryTransfer.Hash(turn.Content)[..12]}"));
                    ambiguous = false;
                }
                break;
            }
        }
        var queries = new List<SearchTerm> { new(question, "semantic") };
        queries.AddRange(exact.Concat(refs.Select(r => r.Text)).Select(s => new SearchTerm(s, "exact")));
        if (Regex.IsMatch(question, @"\b(compar\w*|différen\w*|versus|vs\.?)\b", RegexOptions.IgnoreCase))
        {
            var pieces = Regex.Split(question, @"\s+(?:et|and|avec|versus|vs\.?)\s+", RegexOptions.IgnoreCase);
            if (pieces.Length > 1) queries.AddRange(pieces.Select(s => new SearchTerm(s.Trim(), "semantic")));
        }
        if (facets)
        {
            var brief = PassageClassifier.Brief(question, exact, refs.Select(r => r.Text), Math.Clamp(maximum, 1, 4));
            var expanded = queries.Take(1).Concat(brief.Facets.SelectMany(f => new[] { new SearchTerm(f.Lexical, "lexical"), new SearchTerm(f.Semantic, "semantic") }))
                .Concat(queries.Skip(1)).Where(t => t.Text.Length > 0).DistinctBy(t => (t.Kind, t.Text)).ToArray();
            return new(question, expanded, refs.ToArray(), ambiguous, brief);
        }
        return new(question, queries.DistinctBy(q => q.Text, StringComparer.OrdinalIgnoreCase).Take(maximum).ToArray(), refs.ToArray(), ambiguous);
    }
}
