using System.Text;
using Alycia.Contracts.Context;

namespace Alycia.Modules.Testing;

public sealed class RecordedSummaries(Func<IReadOnlyList<SummarySource>, string> summarize, int maxCalls) : ISummaryReducer
{
    public int Calls { get; private set; }
    public Task<string> ReduceAsync(IReadOnlyList<SummarySource> sources, int targetBytes, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (targetBytes < 128) throw new ArgumentException("Budget de résumé insuffisant.");
        if (sources.Count == 0) return Task.FromResult("Aucun contenu textuel disponible.");
        if (Calls >= maxCalls) throw new ChatModelException("Budget de résumé épuisé.");
        Calls++;
        var reply = summarize(sources.ToArray());
        if (Encoding.UTF8.GetByteCount(reply) > targetBytes) throw new ChatModelException("Résumé hors budget.");
        return Task.FromResult(reply);
    }
}
