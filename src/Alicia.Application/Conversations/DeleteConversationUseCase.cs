using Alicia.Domain.Conversations;

namespace Alicia.Application.Conversations;

public sealed class DeleteConversationUseCase
{
    private readonly IConversationRepository _repository;

    public DeleteConversationUseCase(IConversationRepository repository)
    {
        ArgumentNullException.ThrowIfNull(repository);
        _repository = repository;
    }

    public async Task ExecuteAsync(
        ConversationId conversationId,
        CancellationToken cancellationToken = default)
    {
        if (conversationId.IsEmpty)
        {
            throw new ArgumentException("Conversation identifier cannot be empty.", nameof(conversationId));
        }

        bool deleted = await _repository
            .DeleteAsync(conversationId, cancellationToken)
            .ConfigureAwait(false);

        if (!deleted)
        {
            throw new KeyNotFoundException($"Conversation '{conversationId}' was not found.");
        }
    }
}
