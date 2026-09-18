using Alicia.Domain.Conversations;

namespace Alicia.Application.Generations;

public interface IConversationBranchGenerationSelectionStore :
    IConversationGenerationSelectionStore
{
    Task<ConversationGenerationSelection?> LoadAsync(
        ConversationId conversationId,
        ConversationBranchId branchId,
        CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(
        ConversationId conversationId,
        ConversationBranchId branchId,
        CancellationToken cancellationToken = default);
}
