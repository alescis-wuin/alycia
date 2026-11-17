using System.Text.Json;
using System.Text.RegularExpressions;

namespace Alycia.Modules.Chat.Application;

public sealed record SuppliedSource(string Reference, Guid FileId, string Path, string Sha256, int StartLine, int EndLine,
    int Start, int End, string Origin, bool Partial);
public sealed record CitationReport(string Status, SuppliedSource[] Supplied, string[] Recognized, string[] Unknown);
public sealed record TurnDiagnostics(GenerationUsage? Usage = null, RetrievalReport? Retrieval = null, MemoryReport? Memory = null, CitationReport? Citations = null, IReadOnlyList<string>? Warnings = null)
{
    public static TurnDiagnostics? Read(string json)
    {
        try
        {
            var report = JsonSerializer.Deserialize<TurnDiagnostics>(json, ProjectToolCatalog.Json);
            if (report?.Usage is { } usage && (usage.Calls is null || usage.Calls.Any(c => c is null))) return null;
            if (report?.Citations is { } citations && (citations.Supplied is null || citations.Recognized is null || citations.Unknown is null
                || citations.Supplied.Any(c => c is null))) return null;
            if (report?.Retrieval is { Omissions: null }) return null;
            return report;
        }
        catch (JsonException) { return null; }
    }
}
public static class CitationAudit
{
    private static readonly Regex Markers = new(@"\[(S[0-9a-f]{12})\]", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    public static CitationReport Check(string answer, IReadOnlyList<SourceExcerpt>? excerpts, IReadOnlyList<ToolObservation>? tools)
    {
        var supplied = new List<SuppliedSource>();
        foreach (var source in excerpts ?? [])
            if (source is { Reference: not null, Sha256: not null, StartLine: { } first, EndLine: { } last, Start: { } start, End: { } end })
                supplied.Add(new(source.Reference, source.Id, source.Path, source.Sha256, first, last, start, end, "initial", source.Partial));
        foreach (var observation in tools ?? [])
            if (observation.Succeeded && observation.Evidence is { Reference: not null, FileId: { } id, End: { } end, StartLine: { } first, EndLine: { } last } source)
                supplied.Add(new(source.Reference, id, source.Path, source.Sha256, first, last, source.Offset, end, "tool", !source.CompleteSource));
        var unique = supplied.DistinctBy(s => s.Reference).ToArray();
        var known = unique.Select(s => s.Reference).ToHashSet(StringComparer.Ordinal);
        var markers = Markers.Matches(answer).Select(m => m.Groups[1].Value.ToUpperInvariant()).Distinct(StringComparer.Ordinal).ToArray();
        var unknown = markers.Where(m => !known.Contains(m)).ToArray();
        return new(unknown.Length > 0 ? "unknown_references" : markers.Length > 0 ? "references_recognized" : unique.Length > 0 ? "sources_uncited" : "no_sources",
            unique, markers.Where(known.Contains).ToArray(), unknown);
    }
}
