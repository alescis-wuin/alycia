using Alycia.Contracts.Context;

namespace Alycia.Contracts.Tools;

public interface IToolHistoryReader : ITurnHistory
{
    Task<int?> MessageCountAsync(Guid id, CancellationToken ct);
}
