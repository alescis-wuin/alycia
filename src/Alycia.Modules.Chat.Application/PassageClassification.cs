using System.Text.RegularExpressions;

namespace Alycia.Modules.Chat.Application;

public static class PassageClassification
{
    public static QueryBrief Brief(string question, IEnumerable<string> literals, IEnumerable<string> antecedents, int maxFacets = 4)
    {
        var exact = literals.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var anchors = string.Join(" ", antecedents);
        var constraints = Regex.Matches(question, @"\b(?:sans|sauf|hors|except|without|excluding|ne\s+\S+\s+pas|pas\s+de)\s+[^,;?.!]{1,100}", RegexOptions.IgnoreCase)
            .Select(m => m.Value.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Take(4).ToArray();
        var parts = Regex.Split(question, @"(?<=[?;])\s+|\s+(?:puis|ainsi que|et aussi|and also|then)\s+", RegexOptions.IgnoreCase)
            .Select(s => s.Trim()).Where(s => s.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).Take(maxFacets).ToArray();
        if (parts.Length == 1 && Regex.IsMatch(question, @"\b(compar\w*|versus|différenc\w*)\b", RegexOptions.IgnoreCase))
        {
            var sides = Regex.Split(parts[0], @"\s+(?:et|avec|vs\.?|versus|and)\s+", RegexOptions.IgnoreCase);
            if (sides.Length > 1) parts = sides.Take(maxFacets).ToArray();
        }
        var labels = ProjectRetrieval.Tokens(question).Distinct().Take(16).ToArray();
        var dates = Regex.Matches(question, @"\b\d{4}(?:-\d{2}){0,2}\b").Select(m => m.Value).Distinct().ToArray();
        var facets = parts.Select((s, i) =>
        {
            var terms = Regex.Matches(s, @"[\p{L}\p{N}_./-]+").Select(m => m.Value).Where(t => t.Length > 1).Distinct();
            var literalsAndLimits = exact.Where(x => s.Contains(x, StringComparison.OrdinalIgnoreCase)).Concat(dates).Concat(constraints).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            var lexical = string.Join(' ', terms.Take(32).Concat(literalsAndLimits).Distinct(StringComparer.OrdinalIgnoreCase));
            var missing = literalsAndLimits.Where(x => !s.Contains(x, StringComparison.OrdinalIgnoreCase)).Append(anchors).Where(x => x.Length > 0);
            var semantic = string.Join(" · ", new[] { s }.Concat(missing));
            return new QuestionFacet("F" + (i + 1), s, lexical, semantic);
        }).ToArray();
        var check = Regex.Match(question, """(?:affirmation|hypothèse|assertion|claim)\s*[:=]?\s*[«"]([^»"]+)[»"]""", RegexOptions.IgnoreCase);
        var intent = check.Success ? "verification" : Regex.IsMatch(question, @"\b(compar\w*|versus|différenc\w*)\b", RegexOptions.IgnoreCase) ? "comparison"
            : Regex.IsMatch(question, @"\b(avant|après|before|previous|earlier|quand|when)\b", RegexOptions.IgnoreCase) ? "temporal" : "information";
        var language = Regex.IsMatch(question, @"\b(le|la|les|des|quel\w*|comment|combien|est|comparez|sans|puis)\b", RegexOptions.IgnoreCase) ? "fr"
            : Regex.IsMatch(question, @"\b(the|what|how|which|before|compare|without)\b", RegexOptions.IgnoreCase) ? "en" : "und";
        return new(string.Join(" ", labels.Concat(exact).Concat(constraints).Distinct()), labels, exact, dates, facets,
            check.Success ? check.Groups[1].Value : null, question, constraints, intent, language);
    }

    public static (IReadOnlyList<SourceChunk> Ranked, ClassificationReport Report) Apply(string original, QueryBrief brief,
        IReadOnlyList<SourceChunk> ranked, ChatOptions options, IReadOnlyDictionary<string, double>? semantic = null) =>
        Classify(original, brief, ranked, PassageRankingSettings.Capture(options, options.PinnedSourceIds), semantic);

    public static (IReadOnlyList<SourceChunk> Ranked, ClassificationReport Report) Classify(string original, QueryBrief brief,
        IReadOnlyList<SourceChunk> ranked, PassageRankingSettings options, IReadOnlyDictionary<string, double>? semantic = null)
    {
        var judgments = new List<PassageJudgment>();
        var admitted = new HashSet<string>(); var neutrals = new List<(SourceChunk Chunk, double Score)>();
        var remaining = options.ClassificationBudget;
        var limit = Math.Min(ranked.Count, options.RerankCandidateLimit);
        var originalTerms = Terms(original); var briefTerms = Terms(brief.Summary);
        var facets = brief.Facets.ToDictionary(f => f.Id, f => Terms(f.Semantic));
        for (var i = 0; i < limit && remaining >= facets.Count + 2; i++)
        {
            var chunk = ranked[i]; var reference = ProjectRetrieval.Reference(chunk);
            var content = Terms(chunk.Text + " " + chunk.Path + " " + chunk.Symbol + " " + chunk.Guide?.Terms);
            double Match(HashSet<string> wanted) => wanted.Count == 0 ? 0 : (double)wanted.Count(content.Contains) / wanted.Count;
            var sourceScore = Match(originalTerms); var summaryScore = Match(briefTerms);
            var scores = facets.ToDictionary(p => p.Key, p => Match(p.Value));
            foreach (var facet in brief.Facets)
                if (brief.Literals.Any(x => facet.Question.Contains(x, StringComparison.OrdinalIgnoreCase) &&
                    (chunk.Text.Contains(x, StringComparison.OrdinalIgnoreCase) || chunk.Path.Contains(x, StringComparison.OrdinalIgnoreCase))))
                    scores[facet.Id] = 1;
            var best = Math.Max(Math.Max(sourceScore, summaryScore), scores.Values.DefaultIfEmpty(0).Max());
            var relation = "unknown";
            if (brief.Hypothesis is { } hypothesis)
            {
                var status = SemanticClaimAudit.Check(hypothesis + " [" + reference + "]", [], [new(reference, chunk.Text)]).Findings.FirstOrDefault()?.Status;
                relation = status switch { "supported" => "supports", "contradicted" => "contradicts", _ => "unknown" };
            }
            var relevance = best >= options.RelevantThreshold || relation != "unknown" ? "relevant"
                : best >= options.NeutralThreshold || semantic?.GetValueOrDefault(reference) > 0 ? "uncertain_or_neutral" : "irrelevant";
            remaining -= 2 + facets.Count;
            if (relevance == "relevant" || options.PinnedSourceIds?.Contains(chunk.FileId) == true) admitted.Add(reference);
            else if (relevance == "uncertain_or_neutral") neutrals.Add((chunk, best));
            judgments.Add(new(reference, relevance, relation, sourceScore, summaryScore, scores, false));
        }
        // A budget too small for one complete assessment keeps the original recall.
        if (judgments.Count == 0) return (ranked, new("lexical-facets-v1", 0, ranked.Count, []));
        var neutralLimit = admitted.Count == 0 ? Math.Max(1, options.NeutralCandidates) : options.NeutralCandidates;
        foreach (var candidate in neutrals.OrderByDescending(n => n.Score).DistinctBy(n => n.Chunk.FileId).Take(neutralLimit))
            admitted.Add(ProjectRetrieval.Reference(candidate.Chunk));
        // Unexamined candidates remain eligible when classification cannot cover the shortlist.
        var ordered = ranked.Take(judgments.Count).Where(c => admitted.Contains(ProjectRetrieval.Reference(c))).Concat(ranked.Skip(judgments.Count)).ToArray();
        return (ordered, new("lexical-facets-v1", options.ClassificationBudget - remaining, ranked.Count - judgments.Count,
            judgments.Select(j => j with { Admitted = admitted.Contains(j.Reference) }).ToArray()));
    }

    private static HashSet<string> Terms(string text) => ProjectRetrieval.Tokens(text).ToHashSet(StringComparer.OrdinalIgnoreCase);
}
