using System.Globalization;
using Alycia.Contracts.Context;
using Alycia.Contracts.Memory;

namespace Alycia.Modules.Testing;

public sealed record HistoryGroup(Guid Owner, string Profile, int Sequence, DateTimeOffset Observed, string Text);
public sealed class FixtureHistoryKnowledge(IEnumerable<HistoryGroup> rows) : IHistoryKnowledge
{
    private readonly HistoryGroup[] _rows = rows.ToArray();
    public Task<Dictionary<int, double>> HistoryAsync(Guid owner, string question, string profile, int through, DateTimeOffset before, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var terms = LexicalTerms.Split(question).ToHashSet();
        return Task.FromResult(_rows.Where(x => x.Owner == owner && x.Profile == profile && x.Sequence <= through && x.Observed < before
            && LexicalTerms.Split(x.Text).Any(terms.Contains)).DistinctBy(x => x.Sequence).ToDictionary(x => x.Sequence, _ => 1d));
    }
}
