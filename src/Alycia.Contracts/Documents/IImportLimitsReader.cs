using Alycia.Contracts.Chat;

namespace Alycia.Contracts.Documents;

public interface IImportLimitsReader
{
    Task<ImportLimits> LoadAsync(CancellationToken ct);
}
