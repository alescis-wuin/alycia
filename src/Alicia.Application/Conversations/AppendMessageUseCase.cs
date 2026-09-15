using Alicia.Domain.Conversations;

namespace Alicia.Application.Conversations;

public sealed class AppendMessageUseCase
{
    private readonly IConversationRepository _repository;
    private readonly TimeProvider _timeProvider;

    public AppendMessageUseCase(
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
        MessageRole role,
        string content,
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

        ChatMessage message = new(
            MessageId.New(),
            role,
            content,
            _timeProvider.GetUtcNow());

        conversation.AddMessage(message);

        await _repository
            .SaveAsync(conversation, cancellationToken)
            .ConfigureAwait(false);

        return message;
    }
}
