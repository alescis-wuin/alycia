using Alycia.Contracts.Retrieval;

namespace Alycia.Modules.Testing;

public sealed class RecordedReranker(string identity, Func<string, SourceChunk, double> score) : IPassageReranker
{
    public Task<RerankScores> ScoreAsync(string question, IReadOnlyList<SourceChunk> passages, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var values = new double[passages.Count];
        for (var i = 0; i < passages.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            values[i] = score(question, passages[i]);
        }
        return Task.FromResult(new RerankScores(identity, values));
    }
}
