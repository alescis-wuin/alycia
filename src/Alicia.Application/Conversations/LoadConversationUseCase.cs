using Alicia.Domain.Conversations;

namespace Alicia.Application.Conversations;

public sealed class LoadConversationUseCase
{
    private readonly IConversationRepository _repository;

    public LoadConversationUseCase(IConversationRepository repository)
    {
        ArgumentNullException.ThrowIfNull(repository);
        _repository = repository;
    }

    public async Task<Conversation> ExecuteAsync(
        ConversationId conversationId,
        CancellationToken cancellationToken = default)
    {
        if (conversationId.IsEmpty)
        {
            throw new ArgumentException("Conversation identifier cannot be empty.", nameof(conversationId));
        }

        Conversation? conversation = await _repository
            .FindAsync(conversationId, cancellationToken)
            .ConfigureAwait(false);

        return conversation
            ?? throw new KeyNotFoundException($"Conversation '{conversationId}' was not found.");
    }
}
