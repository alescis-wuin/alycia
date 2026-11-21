using Alycia.Modules.Chat.Domain;

namespace Alycia.Modules.Chat.Application;

public interface ITurnTools
{
    Task<IProjectTools?> SelectAsync(Conversation conversation, ChatOptions settings, IChatModel model, CancellationToken ct);
    Task<bool> AvailableForContextAsync(Conversation conversation, ChatOptions settings, IChatModel model, CancellationToken ct);
    Task<GenerationReply> GenerateAsync(IChatModel measured, IProjectTools? active, Conversation conversation,
        ContextWindow window, ChatOptions settings, Func<ModelDelta, Task>? onDelta, CancellationToken ct);
}

public sealed class TurnTools(IProjectTools? tools = null, ISnapshotProjectTools? snapshots = null) : ITurnTools
{
    public async Task<IProjectTools?> SelectAsync(Conversation conversation, ChatOptions settings, IChatModel model, CancellationToken ct)
    {
        var candidate = conversation.WorkspaceId.HasValue && snapshots is not null ? snapshots : tools;
        return await AvailableAsync(candidate, conversation, settings, model, ct) ? candidate : null;
    }

    // Context preview and explicit memory preparation use the ordinary tools outside the send transaction.
    public Task<bool> AvailableForContextAsync(Conversation conversation, ChatOptions settings, IChatModel model, CancellationToken ct) =>
        AvailableAsync(tools, conversation, settings, model, ct);

    private static async Task<bool> AvailableAsync(IProjectTools? candidate, Conversation conversation, ChatOptions settings,
        IChatModel model, CancellationToken ct) =>
        settings.UseProjectTools && candidate is not null && await candidate.HasSourcesAsync(conversation.Id, ct)
        && await model.SupportsToolsAsync(conversation.Model, ct);

    public async Task<GenerationReply> GenerateAsync(IChatModel measured, IProjectTools? active, Conversation conversation,
        ContextWindow window, ChatOptions settings, Func<ModelDelta, Task>? onDelta, CancellationToken ct)
    {
        if (active is not null)
            return await ProjectAgent.RunAsync(measured, active, conversation.Id, conversation.Model, window, settings, onDelta, ct);
        if (onDelta is not null)
            return await measured.StreamAsync(conversation.Model, window.Messages, settings, onDelta, ct);
        return new(await measured.GenerateAsync(conversation.Model, window.Messages, settings, ct));
    }
}
