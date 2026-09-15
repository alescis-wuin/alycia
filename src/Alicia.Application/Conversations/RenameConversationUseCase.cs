using Alicia.Domain.Conversations;

namespace Alicia.Application.Conversations;

public sealed class RenameConversationUseCase
{
    private readonly IConversationRepository _repository;
    private readonly TimeProvider _timeProvider;

    public RenameConversationUseCase(
        IConversationRepository repository,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _repository = repository;
        _timeProvider = timeProvider;
    }

    public async Task<Conversation> ExecuteAsync(
        ConversationId conversationId,
        string title,
        CancellationToken cancellationToken = default)
    {
        if (conversationId.IsEmpty)
        {
            throw new ArgumentException("Conversation identifier cannot be empty.", nameof(conversationId));
        }

        Conversation? conversation = await _repository
            .FindAsync(conversationId, cancellationToken)
            .ConfigureAwait(false);

        if (conversation is null)
        {
            throw new KeyNotFoundException($"Conversation '{conversationId}' was not found.");
        }

        conversation.Rename(title, _timeProvider.GetUtcNow());

        await _repository
            .SaveAsync(conversation, cancellationToken)
            .ConfigureAwait(false);

        return conversation;
    }
}
