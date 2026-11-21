using System.Text.Json;
using Alycia.Contracts.Chat;

namespace Alycia.Contracts.Tools;

public interface IProjectTools
{
    Task<bool> HasSourcesAsync(Guid conversation, CancellationToken ct);
    Task<ProjectToolResult> CallAsync(Guid conversation, string name, JsonElement arguments, int maxResultBytes, CancellationToken ct);
}

// Executes against the caller’s scoped database transaction.
public interface ISnapshotProjectTools : IProjectTools { }
