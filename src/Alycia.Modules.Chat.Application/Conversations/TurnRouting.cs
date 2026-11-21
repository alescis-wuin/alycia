using Alycia.Contracts.Context;

namespace Alycia.Modules.Chat.Application;

public interface ITurnRouting
{
    Task PrepareAsync(Guid owner, int messageCount, string text, ChatOptions settings, MeteredChatModel measured,
        Func<ModelDelta, Task>? onDelta, CancellationToken ct);
}

public sealed class TurnRouting(IConversationContextReader repository, IProjectRetrieval? retrieval = null, ITemporalMemory? temporal = null) : ITurnRouting
{
    public async Task PrepareAsync(Guid owner, int messageCount, string text, ChatOptions settings, MeteredChatModel measured,
        Func<ModelDelta, Task>? onDelta, CancellationToken ct)
    {
        if (onDelta is not null && retrieval is not null) await onDelta(new("", Status: "Préparation du contexte…"));
        await QueryRouting.PrepareAsync(repository, owner, messageCount, text, settings, retrieval, measured, ct, temporal);
    }
}
