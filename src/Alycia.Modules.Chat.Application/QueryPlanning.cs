using Alycia.Contracts.Documents;
using Alycia.Contracts.Context;
using System.Text.RegularExpressions;
using System.Text.Json;

namespace Alycia.Modules.Chat.Application;

public static class QueryPlanning
{
    private static readonly Regex Literals = new("""`([^`]+)`|«([^»]+)»|"([^"]+)"|\b[\w/-]+(?:\.[\w/-]+)+\b|\b[A-Z][a-z]+(?:[A-Z]\w+)+\b""",
        RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(150));
    private static readonly Regex Anaphora = new(@"\b(cette version|ce fichier|cette méthode|celui-ci|celle-ci|et avant|et ensuite|this (?:version|file|method)|what about it|and before)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(150));
    public static async Task<QueryBrief> RefineAsync(QueryBrief brief, EnrichmentOptions config, MeteredChatModel model, CancellationToken ct)
    {
        var schema = JsonSerializer.SerializeToElement(new
        {
            type = "object",
            required = new[] { "intent" },
            additionalProperties = false,
            properties = new { intent = new { type = "string", maxLength = 240 } }
        });
        const string instruction = "Sélectionne un extrait exact et continu de la question exprimant son intention. Ne déduis aucun référent manquant. JSON uniquement.";
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(config.TimeoutSeconds));
            var local = await model.PrepareOptionsAsync(config.Model, new ChatOptions
            {
                CallPurpose = "query-brief",
                ContextTokens = config.InputTokens,
                MaxOutputTokens = Math.Min(256, config.OutputTokens),
                SafetyTokens = 128,
                TimeoutSeconds = config.TimeoutSeconds,
                GenerateUntilStop = false,
                UseProjectTools = false,
                Temperature = 0,
                Seed = 0,
                ResponseSchema = schema
            }, timeout.Token);
            if (local.ModelIdentity is null || TokenBudget.Count(instruction + brief.Original, local) + 64 > local.ContextTokens - local.MaxOutputTokens - local.SafetyTokens) return brief;
            var output = await model.GenerateAsync(config.Model, [new("system", instruction), new("user", brief.Original)], local, timeout.Token);
            if (output.Length > 2048) return brief;
            using var document = JsonDocument.Parse(output);
            if (document.RootElement.ValueKind != JsonValueKind.Object || document.RootElement.EnumerateObject().Count() != 1
                || !document.RootElement.TryGetProperty("intent", out var intent) || intent.ValueKind != JsonValueKind.String) return brief;
            var summary = intent.GetString()!.Trim();
            if (summary.Length is < 2 or > 240 || !brief.Original.Contains(summary, StringComparison.Ordinal)) return brief;
            return brief with { Summary = string.Join(" ", new[] { summary }.Concat(brief.Constraints ?? []).Concat(brief.Literals).Distinct()), Method = local.ModelIdentity };
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception error) when (error is ChatModelException or HttpRequestException or OperationCanceledException or JsonException) { return brief; }
    }

    public static SearchQueryPlan Build(string question, IReadOnlyList<HistoryMessage> history, IReadOnlyList<TurnFile> sources, int maximum, bool facets = false)
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
        if (facets)
        {
            var brief = PassageClassification.Brief(question, exact, refs.Select(r => r.Text), Math.Clamp(maximum, 1, 4));
            var expanded = queries.Take(1).Concat(brief.Facets.SelectMany(f => new[] { new SearchTerm(f.Lexical, "lexical"), new SearchTerm(f.Semantic, "semantic") }))
                .Concat(queries.Skip(1)).Where(t => t.Text.Length > 0).DistinctBy(t => (t.Kind, t.Text)).ToArray();
            return new(question, expanded, refs.ToArray(), ambiguous, brief);
        }
        return new(question, queries.DistinctBy(q => q.Text, StringComparer.OrdinalIgnoreCase).Take(maximum).ToArray(), refs.ToArray(), ambiguous);
    }
}
