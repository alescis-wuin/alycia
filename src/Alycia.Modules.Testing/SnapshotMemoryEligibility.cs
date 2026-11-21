using System.Runtime.CompilerServices;

namespace Alycia.Modules.Testing;

public sealed record RecordedMemorySource(Guid Owner, int Sequence, DateTimeOffset Observed, string Sha256);

public sealed class SnapshotMemoryEligibility(IEnumerable<RecordedMemorySource> sources) : IMemoryVectorEligibility
{
    private readonly RecordedMemorySource[] _sources = sources.OrderBy(s => s.Sequence).ToArray();

    public async IAsyncEnumerable<MemoryVectorSource[]> PagesAsync(Guid owner, int through, DateTimeOffset before,
        [EnumeratorCancellation] CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        foreach (var page in _sources.Where(s => s.Owner == owner && s.Sequence <= through && s.Observed < before)
            .Select(s => new MemoryVectorSource(s.Sequence, s.Sha256)).Chunk(128))
        {
            ct.ThrowIfCancellationRequested();
            yield return page;
        }
        await Task.CompletedTask;
    }
}
