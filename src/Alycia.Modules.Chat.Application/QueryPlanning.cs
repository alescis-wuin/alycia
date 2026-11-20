using System.Text.RegularExpressions;
using Alycia.Modules.Chat.Domain;

namespace Alycia.Modules.Chat.Application;

public sealed record SearchTerm(string Text, string Kind);
public sealed record ResolvedReferent(string Text, int Sequence, string Reference);
public sealed record SearchQueryPlan(string Original, SearchTerm[] Queries, ResolvedReferent[] Resolved, bool Ambiguous);

public static class QueryPlanning
{
    private static readonly Regex Literals = new("""`([^`]+)`|«([^»]+)»|"([^"]+)"|\b[\w/-]+(?:\.[\w/-]+)+\b|\b[A-Z][a-z]+(?:[A-Z]\w+)+\b""",
        RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(150));
    private static readonly Regex Anaphora = new(@"\b(cette version|ce fichier|cette méthode|celui-ci|celle-ci|et avant|et ensuite|this (?:version|file|method)|what about it|and before)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(150));
    public static SearchQueryPlan Build(string question, IReadOnlyList<ChatMessage> history, IReadOnlyList<Attachment> sources, int maximum)
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
                    refs.Add(new(anchor, turn.Sequence, $"H{turn.Sequence}:{StructuredMemory.Hash(turn.Content)[..12]}"));
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
        return new(question, queries.DistinctBy(q => q.Text, StringComparer.OrdinalIgnoreCase).Take(maximum).ToArray(), refs.ToArray(), ambiguous);
    }
}
