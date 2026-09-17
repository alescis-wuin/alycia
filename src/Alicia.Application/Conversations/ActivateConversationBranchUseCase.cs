using Alicia.Domain.Conversations;

namespace Alicia.Application.Conversations;

public sealed class ActivateConversationBranchUseCase
{
    private readonly IConversationRepository _repository;

    public ActivateConversationBranchUseCase(IConversationRepository repository)
    {
        ArgumentNullException.ThrowIfNull(repository);
        _repository = repository;
    }

    public async Task<Conversation> ExecuteAsync(
        ConversationId conversationId,
        ConversationBranchId branchId,
        CancellationToken cancellationToken = default)
    {
        if (conversationId.IsEmpty)
        {
            throw new ArgumentException(
                "Conversation identifier cannot be empty.",
                nameof(conversationId));
        }

        if (branchId.IsEmpty)
        {
            throw new ArgumentException(
                "Conversation branch identifier cannot be empty.",
                nameof(branchId));
        }

        Conversation? conversation = await _repository
            .FindAsync(conversationId, cancellationToken)
            .ConfigureAwait(false);

        if (conversation is null)
        {
            throw new KeyNotFoundException(
                $"Conversation '{conversationId}' was not found.");
        }

        conversation.ActivateBranch(branchId);

        await _repository
            .SaveAsync(conversation, cancellationToken)
            .ConfigureAwait(false);

        return conversation;
    }
}
