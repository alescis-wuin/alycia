namespace Alycia.Modules.Chat.Application;

public interface ITemporalMemoryIndex
{
    Task RefreshAsync(CancellationToken ct);
    Task<TemporalFact[]> ExportAsync(Guid owner, int after, int limit, CancellationToken ct);
    Task<int> ClearAsync(Guid owner, CancellationToken ct);
}
