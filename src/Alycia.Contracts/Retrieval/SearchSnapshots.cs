using Alycia.Contracts.Indexing;
using System.Collections.Frozen;
using Alycia.Contracts.Chat;

namespace Alycia.Contracts.Retrieval;

public static class SearchSnapshots
{
    public static SourceChunk[]? CopyChunks(IEnumerable<SourceChunk>? chunks) =>
        chunks?.ToArray();

    public static QueryBrief? Copy(QueryBrief? brief) => brief is null ? null : brief with
    {
        Labels = [.. brief.Labels],
        Literals = [.. brief.Literals],
        Dates = [.. brief.Dates],
        Facets = [.. brief.Facets],
        Constraints = brief.Constraints?.ToArray(),
        Categories = brief.Categories?.ToArray()
    };

    public static SearchQueryPlan? Copy(SearchQueryPlan? plan) => plan is null ? null : plan with
    {
        Queries = [.. plan.Queries],
        Resolved = [.. plan.Resolved],
        Brief = Copy(plan.Brief)
    };

    public static RetrievalReport? Copy(RetrievalReport? report) => report is null ? null : report with
    {
        Omissions = report.Omissions.ToArray(),
        Plan = Copy(report.Plan),
        Decisions = report.Decisions?.Select(d => d with { Queries = d.Queries?.ToArray() }).ToArray(),
        Rerank = report.Rerank is { } r ? r with { Scores = r.Scores?.ToFrozenDictionary(StringComparer.Ordinal) } : null,
        Classification = report.Classification is { } c ? c with
        {
            Candidates = c.Candidates.Select(p => p with { Facets = p.Facets.ToFrozenDictionary(StringComparer.Ordinal) }).ToArray()
        } : null
    };
}
