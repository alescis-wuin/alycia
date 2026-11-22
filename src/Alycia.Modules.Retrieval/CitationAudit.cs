using System.Text.Json;
using System.Text.RegularExpressions;

namespace Alycia.Modules.Retrieval;

public static class CitationAudit
{
    private static readonly Regex Markers = new(@"\[(S[0-9a-f]{12}|H[0-9]+:[0-9a-f]{12})\]", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    public static CitationReport Check(string answer, IReadOnlyList<SourceExcerpt>? excerpts, IReadOnlyList<ToolObservation>? tools, IReadOnlyList<HistoryEvidence>? memory = null)
    {
        var supplied = new List<SuppliedSource>();
        foreach (var source in excerpts ?? [])
            if (source is { Reference: not null, Sha256: not null, StartLine: { } first, EndLine: { } last, Start: { } start, End: { } end })
                supplied.Add(new(source.Reference, source.Id, source.Path, source.Sha256, first, last, start, end, "initial", source.Partial, source.Scope, source.Created,
                    source.Modified, source.Uploaded, source.Tokens, source.RankedReference, source.Score));
        foreach (var observation in tools ?? [])
            foreach (var item in (observation.EvidenceItems ?? []).Concat(observation.Evidence is null ? [] : new[] { observation.Evidence }))
                if (observation.Succeeded && item is { Reference: not null, FileId: { } id, End: { } end, StartLine: { } first, EndLine: { } last } source)
                    supplied.Add(new(source.Reference, id, source.Path, source.Sha256, first, last, source.Offset, end, observation.RetainedInFinalPrompt == false ? "tool_compacted" : "tool", true));
        var unique = supplied.OrderBy(s => s.Origin == "tool_compacted").DistinctBy(s => s.Reference).ToArray();
        var known = unique.Where(s => s.Origin != "tool_compacted").Select(s => s.Reference).ToHashSet(StringComparer.Ordinal);
        var historical = (tools ?? []).Where(t => t.Succeeded && t.RetainedInFinalPrompt != false).SelectMany(t => t.History ?? []).Concat(memory ?? []).Distinct().ToArray();
        known.UnionWith(historical.Select(h => h.Reference));
        var markers = Markers.Matches(answer).Select(m => m.Groups[1].Value.ToUpperInvariant()).Distinct(StringComparer.Ordinal).ToArray();
        var unknown = markers.Where(m => !known.Contains(m)).ToArray();
        return new(unknown.Length > 0 ? "unknown_references" : markers.Length > 0 ? "references_recognized" : known.Count > 0 ? "sources_uncited" : "no_sources",
            unique, markers.Where(known.Contains).ToArray(), unknown, historical);
    }
}
