using Alicia.Domain.Conversations;

namespace Alicia.Application.Conversations;

public sealed class EditMessageUseCase
{
    private readonly IConversationRepository _repository;
    private readonly TimeProvider _timeProvider;

    public EditMessageUseCase(
        IConversationRepository repository,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _repository = repository;
        _timeProvider = timeProvider;
    }

    public async Task<ChatMessage> ExecuteAsync(
        ConversationId conversationId,
        MessageRevisionId messageRevisionId,
        string content,
        CancellationToken cancellationToken = default)
    {
        if (conversationId.IsEmpty)
        {
            throw new ArgumentException(
                "Conversation identifier cannot be empty.",
                nameof(conversationId));
        }

        if (messageRevisionId.IsEmpty)
        {
            throw new ArgumentException(
                "Message revision identifier cannot be empty.",
                nameof(messageRevisionId));
        }

        Conversation? conversation = await _repository
            .FindAsync(conversationId, cancellationToken)
            .ConfigureAwait(false);

        if (conversation is null)
        {
            throw new KeyNotFoundException(
                $"Conversation '{conversationId}' was not found.");
        }

        ChatMessage editedRevision = conversation.EditUserMessage(
            messageRevisionId,
            content,
            _timeProvider.GetUtcNow());

        await _repository
            .SaveAsync(conversation, cancellationToken)
            .ConfigureAwait(false);

        return editedRevision;
    }
}
