using System.Text.Json;
using System.Text.RegularExpressions;

namespace Alycia.Modules.Chat.Application;

public sealed record EvidenceText(string Reference, string Text);
public sealed record ClaimFinding(string Text, string[] References, string Status, string Reason);
public sealed record ClaimReport(ClaimFinding[] Findings, int Omitted, string Policy = "heuristic-v1; quotations and lexical overlap do not establish truth");

public static class ClaimAudit
{
    private static readonly Regex Markers = new(@"\[(S[0-9a-f]{12}|H[0-9]+:[0-9a-f]{12})\]", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    private static string Normalize(string text) => Regex.Replace(text.Trim(), @"\s+", " ", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    public static ClaimReport Check(string answer, IReadOnlyList<SourceExcerpt>? sources, IReadOnlyList<EvidenceText>? tools)
    {
        var evidence = (sources ?? []).Where(s => s.Reference is not null && s.Content is not null)
            .Select(s => new EvidenceText(s.Reference!, s.Content!)).Concat(tools ?? [])
            .GroupBy(e => e.Reference, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Select(e => e.Text).ToArray(), StringComparer.OrdinalIgnoreCase);
        var lines = answer.Split('\n', StringSplitOptions.RemoveEmptyEntries).Where(l => l.Trim().Length >= 12).ToArray();
        var findings = new List<ClaimFinding>();
        foreach (var line in lines.Take(64))
        {
            var refs = Markers.Matches(line).Select(m => m.Groups[1].Value.ToUpperInvariant()).Distinct().ToArray();
            var claim = Normalize(Markers.Replace(line, "").Trim(' ', '-', '*', '>', '"', '«', '»'));
            var texts = refs.Where(evidence.ContainsKey).SelectMany(r => evidence[r]).ToArray();
            var status = "not_demonstrated"; var reason = "Aucun passage accessible pour tous les identifiants ; consulter une source ciblée.";
            if (refs.Length > 0 && refs.All(evidence.ContainsKey))
            {
                if (claim.Length >= 12 && texts.Any(t => Normalize(t).Contains(claim, StringComparison.Ordinal)))
                { status = "direct_quote"; reason = "Texte retrouvé littéralement ; ni vérité externe ni approbation utilisateur déduites."; }
                else
                {
                    var terms = ProjectRetrieval.Tokens(claim).Distinct().ToArray();
                    var sourceTerms = texts.SelectMany(ProjectRetrieval.Tokens).ToHashSet(StringComparer.Ordinal);
                    var overlap = terms.Length == 0 ? 0 : (double)terms.Count(sourceTerms.Contains) / terms.Length;
                    var numbers = Regex.Matches(claim, @"\b\d+\b", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)).Select(m => m.Value);
                    if (numbers.Any(n => !texts.Any(t => t.Contains(n, StringComparison.Ordinal))))
                    { status = "uncertain"; reason = "Nombre non retrouvé dans le passage cité ; vérifier avant de conclure."; }
                    else if (terms.Length >= 3 && overlap >= .75)
                    { status = "indirect_lexical"; reason = "Vocabulaire commun seulement : négation, causalité et sens non vérifiés."; }
                    else { status = "uncertain"; reason = "Référence connue mais adéquation de l’affirmation non établie."; }
                }
            }
            findings.Add(new(FileContext.Utf8Prefix(line, 1200), refs, status, reason));
        }
        return new(findings.ToArray(), Math.Max(0, lines.Length - findings.Count));
    }

    public static IReadOnlyList<EvidenceText> ReadToolEvidence(ProjectToolResult result)
    {
        if (result.IsError) return [];
        var allowed = (result.EvidenceItems ?? []).Select(e => e.Reference)
            .Concat(result.Evidence is null ? [] : new[] { result.Evidence.Reference })
            .Concat((result.History ?? []).Select(e => e.Reference)).Where(r => r is not null).ToHashSet(StringComparer.Ordinal);
        var found = new List<EvidenceText>();
        void Visit(JsonElement node)
        {
            if (node.ValueKind == JsonValueKind.Object)
            {
                if (node.TryGetProperty("reference", out var reference) && reference.ValueKind == JsonValueKind.String
                    && allowed.Contains(reference.GetString()) && node.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String)
                    found.Add(new(reference.GetString()!, text.GetString()!));
                foreach (var property in node.EnumerateObject()) Visit(property.Value);
            }
            else if (node.ValueKind == JsonValueKind.Array) foreach (var item in node.EnumerateArray()) Visit(item);
        }
        try { using var parsed = JsonDocument.Parse(result.Text); Visit(parsed.RootElement); }
        catch (JsonException) { }
        return found;
    }
}
