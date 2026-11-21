using System.Text.Json;

namespace Alycia.Modules.Chat.Application;

public sealed record ProjectToolResult(string Text, bool IsError = false, ToolEvidence? Evidence = null, IReadOnlyList<ToolEvidence>? EvidenceItems = null, IReadOnlyList<HistoryEvidence>? History = null);
public interface IProjectTools
{
    Task<bool> HasSourcesAsync(Guid conversation, CancellationToken ct);
    Task<ProjectToolResult> CallAsync(Guid conversation, string name, JsonElement arguments, int maxResultBytes, CancellationToken ct);
}


// Executes against the caller’s scoped database transaction.
public interface ISnapshotProjectTools : IProjectTools { }
