using Alycia.Contracts.Tools;

namespace Alycia.Modules.Chat.Application;

public interface ITurnTools
{
    Task<IProjectTools?> SelectAsync(ToolTurnScope scope, bool enabled, IChatModel model, CancellationToken ct);
    Task<bool> AvailableForContextAsync(ToolTurnScope scope, bool enabled, IChatModel model, CancellationToken ct);
    Task<GenerationReply> GenerateAsync(IChatModel measured, IProjectTools? active, ToolTurnScope scope,
        ContextWindow window, ChatOptions settings, Func<ModelDelta, Task>? onDelta, CancellationToken ct);
}

public sealed class TurnTools(IProjectTools? tools = null, ISnapshotProjectTools? snapshots = null) : ITurnTools
{
    public async Task<IProjectTools?> SelectAsync(ToolTurnScope scope, bool enabled, IChatModel model, CancellationToken ct)
    {
        var candidate = scope.WorkspaceId.HasValue && snapshots is not null ? snapshots : tools;
        return await AvailableAsync(candidate, scope, enabled, model, ct) ? candidate : null;
    }

    // Context preview and explicit memory preparation use the ordinary tools outside the send transaction.
    public Task<bool> AvailableForContextAsync(ToolTurnScope scope, bool enabled, IChatModel model, CancellationToken ct) =>
        AvailableAsync(tools, scope, enabled, model, ct);

    private static async Task<bool> AvailableAsync(IProjectTools? candidate, ToolTurnScope scope, bool enabled,
        IChatModel model, CancellationToken ct) =>
        enabled && candidate is not null && await candidate.HasSourcesAsync(scope.ConversationId, ct)
        && await model.SupportsToolsAsync(scope.Model, ct);

    public async Task<GenerationReply> GenerateAsync(IChatModel measured, IProjectTools? active, ToolTurnScope scope,
        ContextWindow window, ChatOptions settings, Func<ModelDelta, Task>? onDelta, CancellationToken ct)
    {
        if (active is not null)
            return await ProjectAgent.RunAsync(measured, active, scope.ConversationId, scope.Model, window, settings, onDelta, ct);
        if (onDelta is not null)
            return await measured.StreamAsync(scope.Model, window.Messages, settings, onDelta, ct);
        return new(await measured.GenerateAsync(scope.Model, window.Messages, settings, ct));
    }
}
