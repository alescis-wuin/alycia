using System.Text.Json;

namespace Alycia.Modules.Chat.Application;

public interface IProjectTools
{
    Task<bool> HasSourcesAsync(Guid conversation, CancellationToken ct);
    Task<ProjectToolResult> CallAsync(Guid conversation, string name, JsonElement arguments, int maxResultBytes, CancellationToken ct);
}


// Executes against the caller’s scoped database transaction.
public interface ISnapshotProjectTools : IProjectTools { }
