namespace Alycia.Modules.Testing;

public sealed class RecordedQueryBrief(Func<string, string?> selection, string identity) : IQueryBriefRefiner
{
    public Task<QueryBrief> RefineAsync(QueryBrief brief, BriefGeneration settings, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var copy = SearchSnapshots.Copy(brief)!;
        var picked = selection(copy.Original)?.Trim();
        if (picked is not { Length: >= 2 and <= 240 } || !copy.Original.Contains(picked, StringComparison.Ordinal))
            return Task.FromResult(copy);
        var tokens = new[] { picked }.Concat(copy.Constraints ?? []).Concat(copy.Literals);
        return Task.FromResult(copy with { Summary = string.Join(" ", tokens.Distinct()), Method = identity });
    }
}
