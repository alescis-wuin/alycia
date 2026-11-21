using System.Text.RegularExpressions;

namespace Alycia.Modules.Retrieval;

public static class PassageEvidenceAudit
{
    private static readonly Regex Marker = new(@"\[([SH][0-9a-f][0-9a-z:._-]*)\]", RegexOptions.IgnoreCase);
    private static readonly Regex Number = new(@"(?<![\p{L}\p{N}_])\d+(?:[.,:/-]\d+)*(?![\p{L}\p{N}_])");
    private static readonly Regex Negation = new(@"\b(non|not|never|jamais|aucun\w*|interdit\w*|désactiv\w*|disabled|false|pas)\b", RegexOptions.IgnoreCase);
    private static readonly Regex UserAttribution = new(@"\b(vous (?:avez|aviez)|tu (?:as|avais)|(?:l.user|l.utilisateur|the user) (?:a|chose|decided))\b", RegexOptions.IgnoreCase);
    private static readonly Regex Quoted = new("""[«"]([^»"]{2,})[»"]""");
    private static readonly Regex Identifier = new(@"(?:\b[A-Za-z]\w*(?:\.\w+)+\b|\b[A-Z][a-z]+(?:[A-Z]\w+)+\b)");
    private static readonly Dictionary<string, string> Equivalents = new(StringComparer.OrdinalIgnoreCase)
    {
        ["rétention"] = "conservation",
        ["conserve"] = "conservation",
        ["conservés"] = "conservation",
        ["conservé"] = "conservation",
        ["journaux"] = "journal",
        ["days"] = "jour",
        ["jours"] = "jour",
        ["minutes"] = "minute",
        ["authorized"] = "autorisé",
        ["enabled"] = "activé"
    };
    private static HashSet<string> Terms(string s) => PassageSearch.Tokens(s).Select(t => Equivalents.GetValueOrDefault(t, t)).ToHashSet();
    private static string Clean(string s) => Regex.Replace(s.Trim(' ', '\t', '.', '-', '*', '«', '»', '"'), @"\s+", " ").Trim();
    public static string ClaimText(string text) => Clean(Marker.Replace(text, ""));
    private static HashSet<string> Numbers(string s) => Number.Matches(s).Select(m => m.Value.Replace(',', '.')).ToHashSet();
    public static ClaimReport Check(string answer, IReadOnlyList<SourceExcerpt>? sources, IReadOnlyList<EvidenceText>? other)
    {
        var evidence = (sources ?? []).Where(s => s is { Reference: not null, Content: not null } && s.SourceType != "summary")
            .Select(s => new EvidenceText(s.Reference!, s.Content!)).Concat(other ?? []).ToLookup(x => x.Reference, StringComparer.OrdinalIgnoreCase);
        var lines = Regex.Split(answer, @"\n+|(?<=[.!?])\s+(?=[A-ZÀ-Ý])").Where(l => l.Trim().Length > 1).ToArray();
        var findings = lines.Take(64).Select(line =>
        {
            var references = Marker.Matches(line).Select(m => m.Groups[1].Value.ToUpperInvariant()).Distinct().ToArray();
            var claim = Clean(Marker.Replace(line, ""));
            ClaimFinding Finding(string state, string why) => new(Alycia.Contracts.Documents.Utf8Text.Prefix(line, 900), references, state, why);
            if (references.Any(r => !evidence.Contains(r))) return Finding("insufficient", "Référence inconnue ou dépourvue de texte original.");
            if (references.Length == 0)
            {
                var factual = Number.IsMatch(claim) || Identifier.IsMatch(claim) || UserAttribution.IsMatch(claim)
                    || Regex.IsMatch(claim, @"\b(est|sont|doit|utilise|contient|is|are|uses|requires)\b", RegexOptions.IgnoreCase);
                return factual && !claim.EndsWith('?') ? Finding("insufficient", "Affirmation sans passage cité.")
                    : Finding("not_verifiable", "Question, suggestion ou texte sans affirmation vérifiable identifiée.");
            }
            var candidates = references.SelectMany(r => evidence[r]).Where(e => !UserAttribution.IsMatch(claim) || e.Role == "user").ToArray();
            if (candidates.Length == 0) return Finding("insufficient", "La décision attribuée à l’utilisateur exige une déclaration de rôle utilisateur.");
            var numbers = Numbers(claim);
            var identifiers = Identifier.Matches(claim).Select(m => m.Value).ToArray();
            var quotes = Quoted.Matches(claim).Select(m => m.Groups[1].Value).ToArray();
            var terms = Terms(claim);
            var opposed = false; var supported = false;
            foreach (var passage in candidates)
            {
                foreach (var raw in Regex.Split(passage.Text, @"\n+|(?<=[.!?])\s+"))
                {
                    var sentence = Clean(raw);
                    if (sentence.Length < 2) continue;
                    if (string.Equals(sentence, claim, StringComparison.OrdinalIgnoreCase)) { supported = true; continue; }
                    var matched = Terms(sentence);
                    var content = terms.Where(t => !Number.IsMatch(t) && !Negation.IsMatch(t)).ToArray();
                    var common = content.Count(matched.Contains);
                    var sameSubject = common >= 2 && common >= .7 * content.Length && identifiers.All(id => sentence.Contains(id, StringComparison.Ordinal));
                    if (!sameSubject) continue;
                    var values = Numbers(sentence);
                    var oppositePolarity = Negation.IsMatch(claim) != Negation.IsMatch(sentence);
                    var conflictingValue = numbers.Count == 1 && values.Count == 1 && !numbers.SetEquals(values);
                    // Shared words never suffice to confirm a paraphrase.
                    if (oppositePolarity || conflictingValue) opposed = true;
                    else if (quotes.Length > 0 && quotes.All(q => sentence.Contains(q, StringComparison.Ordinal)) && numbers.IsSubsetOf(values)
                        && quotes.Any(q => Clean(q).Equals(claim, StringComparison.Ordinal))) supported = true;
                    else if (terms.SetEquals(matched) && numbers.SetEquals(values) && quotes.All(q => sentence.Contains(q, StringComparison.Ordinal))
                        && PassageSearch.Tokens(claim).Select(t => Equivalents.GetValueOrDefault(t, t))
                            .SequenceEqual(PassageSearch.Tokens(sentence).Select(t => Equivalents.GetValueOrDefault(t, t)))) supported = true;
                }
            }
            if (opposed && supported) return Finding("insufficient", "Les passages cités comportent des variantes incompatibles.");
            if (opposed) return Finding("contradicted", "Même sujet avec polarité opposée ou valeur numérique incompatible dans le passage cité.");
            return supported ? Finding("supported", "Affirmation retrouvée dans un original cité.")
                : Finding("insufficient", "La citation ne démontre pas toute l’affirmation.");
        }).ToArray();
        return new(findings, Math.Max(0, lines.Length - findings.Length), "semantic-rules-v1");
    }
}
