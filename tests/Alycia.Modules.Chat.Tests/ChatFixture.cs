using Alycia.Modules.Chat.Application;
using Alycia.Modules.Chat.Application.Context;

namespace Alycia.Modules.Chat.Tests;

internal static class ChatFixture
{
    public static ChatService Create(IChatRepository repository, IChatModel model, ChatOptions options) =>
        new(repository, model, options, new TurnContext(repository), new ConversationMemory(repository),
            new TurnTools(), new TurnRouting(repository), new TurnResponseAudit());
}
