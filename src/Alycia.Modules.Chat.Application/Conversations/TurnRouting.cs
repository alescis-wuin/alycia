using Alycia.Modules.Chat.Domain;

namespace Alycia.Modules.Chat.Application;

public interface ITurnRouting
{
    Task PrepareAsync(Conversation conversation, string text, ChatOptions settings, MeteredChatModel measured,
        Func<ModelDelta, Task>? onDelta, CancellationToken ct);
}

public sealed class TurnRouting(IChatRepository repository, IProjectRetrieval? retrieval = null, ITemporalMemory? temporal = null) : ITurnRouting
{
    public async Task PrepareAsync(Conversation conversation, string text, ChatOptions settings, MeteredChatModel measured,
        Func<ModelDelta, Task>? onDelta, CancellationToken ct)
    {
        if (onDelta is not null && retrieval is not null) await onDelta(new("", Status: "Préparation du contexte…"));
        await QueryRouting.PrepareAsync(repository, conversation, text, settings, retrieval, measured, ct, temporal);
    }
}
