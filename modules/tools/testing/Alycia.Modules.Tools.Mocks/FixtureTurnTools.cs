using System.Text.RegularExpressions;
using Alycia.Contracts.Context;
using Alycia.Contracts.Tools;

namespace Alycia.Modules.Testing;

public sealed class FixtureTurnTools(IProjectTools? tools = null, ISnapshotProjectTools? snapshots = null,
    Func<IProjectTools, ToolTurnScope, ToolPrompt, CancellationToken, Task<GenerationReply>>? run = null) : ITurnTools
{
    public async Task<IProjectTools?> SelectAsync(ToolTurnScope scope, bool enabled, IChatModel model, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var selected = scope.WorkspaceId.HasValue && snapshots is not null ? snapshots : tools;
        return enabled && selected is not null && await selected.HasSourcesAsync(scope.ConversationId, ct) && await model.SupportsToolsAsync(scope.Model, ct) ? selected : null;
    }
    public Task<bool> AvailableForContextAsync(ToolTurnScope scope, bool enabled, IChatModel model, CancellationToken ct) =>
        AvailableAsync(scope, enabled, model, ct);
    private async Task<bool> AvailableAsync(ToolTurnScope scope, bool enabled, IChatModel model, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return enabled && tools is not null && await tools.HasSourcesAsync(scope.ConversationId, ct) && await model.SupportsToolsAsync(scope.Model, ct);
    }
    public async Task<GenerationReply> GenerateAsync(IChatModel measured, IProjectTools? active, ToolTurnScope scope, ToolPrompt window,
        ToolGeneration settings, Func<ModelDelta, Task>? onDelta, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (active is not null) return await (run ?? throw new InvalidOperationException("Spécifier un scénario d’outil."))(active, scope, window, ct);
        if (onDelta is null) return new(await measured.GenerateAsync(scope.Model, window.Messages, settings.Inference, ct));
        var reply = await measured.StreamAsync(scope.Model, window.Messages, settings.Inference, onDelta, ct);
        return new(reply.Content, reply.Stats, reply.ToolCalls, reply.Thinking);
    }
}
