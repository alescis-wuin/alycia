using Alycia.Contracts.Chat;
using System.Text.Json;

namespace Alycia.Contracts.Tools;

public enum ToolRefusal { InvalidArguments, ForbiddenScope, UnavailableSource, Duplicate, RemoteFailure }

public sealed record ProjectToolResult(string Text, bool IsError = false, ToolEvidence? Evidence = null, IReadOnlyList<ToolEvidence>? EvidenceItems = null, IReadOnlyList<HistoryEvidence>? History = null)
{
    [System.Text.Json.Serialization.JsonIgnore]
    public ToolRefusal? Refusal { get; init; } = IsError ? ToolRefusal.RemoteFailure : null;
}

public interface ISourceCommands
{
    Task<ProjectToolResult> ExecuteAsync(Guid conversation, string name, JsonElement arguments, int bytes, CancellationToken ct);
}
