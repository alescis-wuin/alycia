using Alycia.Modules.Chat.Application.Context;

namespace Alycia.Modules.Chat.Application.Conversations;

public sealed class ConversationTemporalMemory(IChatRepository repository, ITemporalMemoryIndex store)
{
    public async Task<TemporalFact[]> ListAsync(Guid id, int? after, int? limit, CancellationToken ct)
    {
        _ = await repository.FindAsync(id, ct) ?? throw new ConversationNotFoundException();
        if (after is < 0 || limit is < 1 or > 200) throw new ArgumentException("Page de mémoire invalide.");
        return await store.ExportAsync(id, after ?? 0, limit ?? 50, ct);
    }

    public async Task ClearAsync(Guid id, CancellationToken ct)
    {
        _ = await repository.FindAsync(id, ct) ?? throw new ConversationNotFoundException();
        await store.ClearAsync(id, ct);
    }
}
