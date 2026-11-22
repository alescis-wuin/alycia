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
public sealed class FixtureIndexMaintenance(Func<CancellationToken, Task> refresh, Func<CancellationToken, Task> process) : IIndexMaintenance
{
    public async Task RunCycleAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        await refresh(ct);
        ct.ThrowIfCancellationRequested();
        await process(ct);
    }
}

public sealed record RecordedPassage(Guid Owner, Guid File, string Digest, string Model, double Score, int? Sequence = null, DateTimeOffset? Observed = null);
public sealed class RecordedIndexedEmbeddings(IEnumerable<RecordedPassage> passages) : IIndexedEmbeddings
{
    private readonly RecordedPassage[] _passages = passages.ToArray();
    public Task<SemanticSearch> SearchAsync(Guid owner, string query, EmbeddingQuery settings, IReadOnlyList<SourceChunk> chunks, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var found = chunks.Select(c => _passages.FirstOrDefault(p => p.Owner == owner && p.Model == settings.Model && p.File == c.FileId && p.Digest == c.Sha256)).ToArray();
        return Task.FromResult(new SemanticSearch(found.Select(p => p?.Score ?? 0).ToArray(), found.Count(p => p is not null), []));
    }
    public Task<MemoryVectorMatches> SearchMemoryAsync(Guid owner, string question, EmbeddingQuery settings, int through, DateTimeOffset before, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(new MemoryVectorMatches(_passages.Where(p => p.Owner == owner && p.Model == settings.Model && p.Sequence <= through && p.Observed < before)
            .GroupBy(p => p.Sequence!.Value).ToDictionary(g => g.Key, g => g.Max(p => p.Score)), []));
    }
    public Task<SemanticSearch> SearchHistoryAsync(Guid owner, string query, EmbeddingQuery settings, IReadOnlyList<HistoryMessage> messages, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (messages.Any(m => m.ConversationId != owner)) throw new ArgumentException("Historique hors de la conversation.");
        return SearchAsync(owner, query, settings, messages.Select(m => new SourceChunk(m.RequestId, "", MemoryTransfer.Hash(m.Content), "", "history", 0, m.Content.Length, 1, 1, m.Content, [], true)).ToArray(), ct);
    }
}
