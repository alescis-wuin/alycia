using System.Globalization;
using Alycia.Contracts.Context;
using Alycia.Contracts.Memory;

namespace Alycia.Modules.Testing;

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
