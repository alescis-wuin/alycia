namespace Alycia.Modules.Chat.Application;

public interface IIndexMaintenance
{
    Task RunCycleAsync(CancellationToken ct);
}
