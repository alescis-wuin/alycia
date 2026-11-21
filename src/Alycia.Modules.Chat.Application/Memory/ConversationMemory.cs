using Alycia.Modules.Chat.Domain;

namespace Alycia.Modules.Chat.Application;

public sealed record PreparedMemoryChange(string Memory, int Through, string SourceHash, string? LedgerJson);

public interface IConversationMemory
{
    bool Apply(Conversation conversation, PreparedMemoryChange? prepared);
    Task<Conversation> ResetAsync(Guid id, int revision, CancellationToken ct);
}

public sealed class ConversationMemory(IChatRepository repository, GenerationGate gate) : IConversationMemory
{
    // The caller persists a prepared change together with its turn or context preparation.
    public bool Apply(Conversation conversation, PreparedMemoryChange? prepared)
    {
        if (prepared is null) return false;
        if (prepared.LedgerJson is not null)
            conversation.SetMemoryLedger(prepared.LedgerJson);
        else
            conversation.SetMemory(prepared.Memory, prepared.Through, prepared.SourceHash, conversation.Model);
        return true;
    }

    public async Task<Conversation> ResetAsync(Guid id, int revision, CancellationToken ct)
    {
        using var lease = gate.Enter(id);
        var conversation = await repository.FindAsync(id, ct) ?? throw new ConversationNotFoundException();
        ConversationConfiguration.CheckRevision(conversation, revision);
        conversation.SetMemory("", 0, "", "");
        conversation.SetMemoryLedger("{}");
        await repository.SaveAsync(ct);
        return conversation;
    }
}
