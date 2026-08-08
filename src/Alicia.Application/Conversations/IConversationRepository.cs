using Alicia.Domain.Conversations;

namespace Alicia.Application.Conversations;

public interface IConversationRepository
{
    Task<Conversation?> FindAsync(
        ConversationId conversationId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<ConversationSummary>> ListAsync(
        CancellationToken cancellationToken);

    Task SaveAsync(
        Conversation conversation,
        CancellationToken cancellationToken);

    Task<bool> DeleteAsync(
        ConversationId conversationId,
        CancellationToken cancellationToken);
}
