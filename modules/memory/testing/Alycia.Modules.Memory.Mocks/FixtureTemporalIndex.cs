using System.Globalization;
using Alycia.Contracts.Context;
using Alycia.Contracts.Memory;

namespace Alycia.Modules.Testing;

public sealed class FixtureTemporalIndex(IEnumerable<(Guid Owner, TemporalFact Fact)> sources) : ITemporalMemoryIndex, ITemporalMemory
{
    private readonly (Guid Owner, TemporalFact Fact)[] _sources = sources.ToArray();
    private readonly HashSet<(Guid Owner, TemporalFact Fact)> _indexed = [];
    public Task RefreshAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        _indexed.UnionWith(_sources);
        return Task.CompletedTask;
    }
    public Task<TemporalFact[]> ExportAsync(Guid owner, int after, int limit, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(_indexed.Where(x => x.Owner == owner && x.Fact.Sequence > after).GroupBy(x => x.Fact.Sequence)
            .OrderBy(g => g.Key).Take(limit).SelectMany(g => g.Select(x => x.Fact)).ToArray());
    }
    public Task<int> ClearAsync(Guid owner, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var rows = _indexed.Where(x => x.Owner == owner).Select(x => x.Fact.Sequence).Distinct().Count();
        _indexed.RemoveWhere(x => x.Owner == owner);
        return Task.FromResult(rows);
    }
    public Task<TemporalRecall> RecallAsync(Guid owner, string question, TemporalRecallSettings settings, int through, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var rows = _indexed.Where(x => x.Owner == owner && x.Fact.Sequence <= through).Select(x => x.Fact).ToArray();
        var date = question.Split(' ').Last().TrimEnd('?');
        var until = DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? new DateTimeOffset(d.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)).AddDays(question.Contains("avant ", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            : DateTimeOffset.MaxValue;
        var keys = rows.Where(f => f.Observed < until && (question.Contains(f.Key, StringComparison.OrdinalIgnoreCase)
            || LexicalTerms.Split(question).Intersect(LexicalTerms.Split(f.Quote)).Any())).Select(f => f.Key).ToHashSet();
        return Task.FromResult(new TemporalRecall(rows.Where(f => f.Observed < until && keys.Contains(f.Key)).OrderBy(f => f.Sequence),
            rows.Select(f => f.Sequence).Distinct().Count(), through, []));
    }
}
