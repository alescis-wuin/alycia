namespace Alycia.Contracts.Indexing;

public interface IIndexMaintenance
{
    Task RunCycleAsync(CancellationToken ct);
}
