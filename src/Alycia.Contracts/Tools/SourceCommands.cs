using Alycia.Contracts.Chat;
using System.Text.Json;

namespace Alycia.Contracts.Tools;

public sealed record ProjectToolResult(string Text, bool IsError = false, ToolEvidence? Evidence = null, IReadOnlyList<ToolEvidence>? EvidenceItems = null, IReadOnlyList<HistoryEvidence>? History = null);

public interface ISourceCommands
{
    Task<ProjectToolResult> ExecuteAsync(Guid conversation, string name, JsonElement arguments, int bytes, CancellationToken ct);
}
