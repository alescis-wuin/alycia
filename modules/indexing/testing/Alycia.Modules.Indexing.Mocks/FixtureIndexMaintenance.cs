using System.Globalization;
using Alycia.Contracts.Context;
using Alycia.Contracts.Memory;

namespace Alycia.Modules.Testing;

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
