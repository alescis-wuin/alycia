using Alicia.Domain.Conversations;

namespace Alicia.Application.Generations;

public interface IConversationGenerationSelectionStore
{
    Task<ConversationGenerationSelection?> LoadAsync(
        ConversationId conversationId,
        CancellationToken cancellationToken = default);

    Task SaveAsync(
        ConversationGenerationSelection selection,
        CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(
        ConversationId conversationId,
        CancellationToken cancellationToken = default);
}
