using System.Text.Json;

namespace Alycia.Contracts.Chat;

public sealed record UsedGuide(string Reference, string OriginalReference, Guid FileId, string Identity, string State,
    int Tokens, string? Created, string? Modified, DateTimeOffset? Uploaded, string? Unit, string? Sha256 = null, string? Path = null, int Start = 0, int End = 0, bool Partial = false);
public sealed record SourceExcerpt(Guid Id, string Path, int Utf8Bytes, bool Partial, string Reason, string? Reference = null, int? StartLine = null, int? EndLine = null, string? Sha256 = null, int? Start = null, int? End = null,
    double? Score = null, string ScoreKind = "unavailable", string SourceType = "text", bool? CompleteSource = null,
    [property: System.Text.Json.Serialization.JsonIgnore] string? Content = null, string? Scope = null, string? Created = null,
    string? Modified = null, DateTimeOffset? Uploaded = null, int? Tokens = null, string? RankedReference = null);
public sealed record EvidenceDecision(string Reference, Guid FileId, string Reason, int Tokens);
public sealed record EvidenceAssembly(int Budget, int Used, int Files, EvidenceDecision[] Decisions, int Undetailed);
public sealed record SuppliedSource(string Reference, Guid FileId, string Path, string Sha256, int StartLine, int EndLine,
    int Start, int End, string Origin, bool Partial, string? Scope = null, string? Created = null, string? Modified = null,
    DateTimeOffset? Uploaded = null, int? Tokens = null, string? RankedReference = null, double? Score = null);
public sealed record CitationReport(string Status, SuppliedSource[] Supplied, string[] Recognized, string[] Unknown, HistoryEvidence[]? Historical = null);
public sealed record ClaimFinding(string Text, string[] References, string Status, string Reason);
public sealed record ClaimReport(ClaimFinding[] Findings, int Omitted, string Policy = "heuristic-v1; quotations and lexical overlap do not establish truth");
public sealed record VerificationFollowup(string Status, string? Claim, string[] References, SourceExcerpt[]? Sources = null);
public sealed record ResearchGoal(string Query, string State, string[] References);
public sealed record ResearchReport(string StopReason, int CallLimit, int Calls, ResearchGoal[] Goals);
public sealed record TurnDiagnostics(GenerationUsage? Usage = null, RetrievalReport? Retrieval = null, MemoryReport? Memory = null, CitationReport? Citations = null, IReadOnlyList<string>? Warnings = null, ClaimReport? Claims = null, Guid? WorkspaceId = null, int? WorkspaceRevision = null, QueryRoute? Route = null, EvidenceAssembly? Assembly = null, TemporalReport? Temporal = null, ResearchReport? Research = null, VerificationFollowup? Verification = null, UsedGuide[]? Guides = null, int? HistoryMessages = null, bool? HasSystemInstructions = null)
{
    public static TurnDiagnostics? Read(string json)
    {
        try
        {
            var report = JsonSerializer.Deserialize<TurnDiagnostics>(json, ChatJson.Options);
            if (report?.Usage is { } usage && (usage.Calls is null || usage.Calls.Any(c => c is null))) return null;
            if (report?.Citations is { } citations && (citations.Supplied is null || citations.Recognized is null || citations.Unknown is null
                || citations.Supplied.Any(c => c is null))) return null;
            if (report?.Retrieval is { Omissions: null } || report?.Retrieval?.Classification is { } ranking
                && (ranking.Candidates is null || ranking.Candidates.Any(c => c is null || c.Facets is null))) return null;
            if (report?.Guides?.Any(g => g is null || g.Reference is null || g.Start < 0 || g.End < g.Start) == true) return null;
            if (report?.Citations?.Historical?.Any(h => h is null) == true) return null;
            if (report?.Claims is { } claims && (claims.Findings is null || claims.Findings.Any(f => f is null || f.References is null))) return null;
            return report;
        }
        catch (JsonException) { return null; }
    }
}
