using Alycia.Contracts.Context;
using Alycia.Modules.Chat.Domain;

namespace Alycia.Modules.Chat.Application;

public static class ConversationSnapshot
{
    public static ContextConversation ForContext(this Conversation owner, ChatOptions options) =>
        new(owner.Id, owner.MessageCount, owner.Model, ConversationConfiguration.System(owner, options),
            owner.MemoryText, owner.MemoryThroughSequence, owner.MemorySourceHash, owner.MemoryModel, owner.MemoryLedgerJson);
}
