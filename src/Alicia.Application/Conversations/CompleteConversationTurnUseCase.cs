using Alicia.Domain.Conversations;

namespace Alicia.Application.Conversations;

public sealed class CompleteConversationTurnUseCase
{
    private readonly IConversationRepository _repository;
    private readonly IConversationResponder _responder;
    private readonly TimeProvider _timeProvider;

    public CompleteConversationTurnUseCase(
        IConversationRepository repository,
        IConversationResponder responder,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(responder);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _repository = repository;
        _responder = responder;
        _timeProvider = timeProvider;
    }

    public async Task<ChatMessage> ExecuteAsync(
        ConversationId conversationId,
        MessageId triggeringUserMessageId,
        CancellationToken cancellationToken = default)
    {
        if (conversationId.IsEmpty)
        {
            throw new ArgumentException(
                "Conversation identifier cannot be empty.",
                nameof(conversationId));
        }

        if (triggeringUserMessageId.IsEmpty)
        {
            throw new ArgumentException(
                "Triggering message identifier cannot be empty.",
                nameof(triggeringUserMessageId));
        }

        Conversation? conversation = await _repository
            .FindAsync(conversationId, cancellationToken)
            .ConfigureAwait(false);

        if (conversation is null)
        {
            throw new KeyNotFoundException(
                $"Conversation '{conversationId}' was not found.");
        }

        ConversationTurnSnapshot snapshot = ConversationTurnSnapshot.Capture(
            conversation,
            triggeringUserMessageId);
        ConversationResponseRequest request =
            ConversationResponseRequest.FromConversation(conversation);

        ConversationResponse? response = await _responder
            .GenerateAsync(request, cancellationToken)
            .ConfigureAwait(false);

        if (response is null)
        {
            throw new InvalidOperationException(
                "Conversation responder returned no response.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        Conversation? reloadedConversation = await _repository
            .FindAsync(conversationId, cancellationToken)
            .ConfigureAwait(false);
        Conversation currentConversation = snapshot.RequireMatching(reloadedConversation);

        ChatMessage assistantMessage = new(
            MessageId.New(),
            MessageRole.Assistant,
            response.Content,
            _timeProvider.GetUtcNow());

        currentConversation.AddMessage(assistantMessage);

        await _repository
            .SaveAsync(currentConversation, cancellationToken)
            .ConfigureAwait(false);

        return assistantMessage;
    }
}
