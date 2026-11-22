namespace Alycia.Contracts.Indexing;

public sealed record MemoryVectorSource(int Sequence, string Sha256);
public interface IMemoryVectorEligibility
{
    IAsyncEnumerable<MemoryVectorSource[]> PagesAsync(Guid owner, int through, DateTimeOffset before, CancellationToken ct);
}
