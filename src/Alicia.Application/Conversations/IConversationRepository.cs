using Alicia.Domain.Conversations;

namespace Alicia.Application.Conversations;

public interface IConversationRepository
{
    Task<Conversation?> FindAsync(
        ConversationId conversationId,
        CancellationToken cancellationToken);

    Task SaveAsync(
        Conversation conversation,
        CancellationToken cancellationToken);
}
