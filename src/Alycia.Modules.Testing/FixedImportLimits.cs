using Alycia.Contracts.Documents;

namespace Alycia.Modules.Testing;

public sealed class FixedImportLimits(ImportLimits limits) : IImportLimitsReader
{
    public Task<ImportLimits> LoadAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        limits.Validate();
        return Task.FromResult(limits);
    }
}
