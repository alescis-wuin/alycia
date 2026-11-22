using Alycia.Contracts.Tools;

namespace Alycia.Modules.Tools;

public sealed class TurnTools(CreateSummaryReducer summaries, IProjectTools? tools = null, ISnapshotProjectTools? snapshots = null) : ITurnTools
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
        IChatModel model, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return enabled && candidate is not null && await candidate.HasSourcesAsync(scope.ConversationId, ct)
            && await model.SupportsToolsAsync(scope.Model, ct);
    }

    public async Task<GenerationReply> GenerateAsync(IChatModel measured, IProjectTools? active, ToolTurnScope scope,
        ToolPrompt window, ToolGeneration settings, Func<ModelDelta, Task>? onDelta, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (active is not null)
            return await ProjectAgent.RunAsync(summaries, measured, active, scope.ConversationId, scope.Model, window, settings, onDelta, ct);
        if (onDelta is not null)
        {
            var reply = await measured.StreamAsync(scope.Model, window.Messages, settings.Inference, onDelta, ct);
            return new(reply.Content, reply.Stats, reply.ToolCalls, reply.Thinking);
        }
        return new(await measured.GenerateAsync(scope.Model, window.Messages, settings.Inference, ct));
    }
}
