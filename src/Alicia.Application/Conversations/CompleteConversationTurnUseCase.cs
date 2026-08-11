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

        ValidateTrigger(conversation, triggeringUserMessageId);

        ConversationSnapshot snapshot = ConversationSnapshot.Capture(conversation);
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

        Conversation? currentConversation = await _repository
            .FindAsync(conversationId, cancellationToken)
            .ConfigureAwait(false);

        if (currentConversation is null)
        {
            throw new InvalidOperationException(
                "Conversation changed while Alicia was responding. Retry from the latest history.");
        }

        if (!snapshot.Matches(currentConversation))
        {
            throw new InvalidOperationException(
                "Conversation changed while Alicia was responding. Retry from the latest history.");
        }

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

    private static void ValidateTrigger(
        Conversation conversation,
        MessageId triggeringUserMessageId)
    {
        int triggerIndex = -1;

        for (int index = 0; index < conversation.Messages.Count; index++)
        {
            if (conversation.Messages[index].Id == triggeringUserMessageId)
            {
                triggerIndex = index;
                break;
            }
        }

        if (triggerIndex < 0)
        {
            throw new KeyNotFoundException(
                $"Message '{triggeringUserMessageId}' was not found in conversation '{conversation.Id}'.");
        }

        ChatMessage trigger = conversation.Messages[triggerIndex];

        if (trigger.Role != MessageRole.User)
        {
            throw new InvalidOperationException(
                "Only a user message can trigger an assistant response.");
        }

        if (triggerIndex != conversation.Messages.Count - 1)
        {
            throw new InvalidOperationException(
                "The triggering user message is no longer the latest unanswered message.");
        }
    }

    private sealed class ConversationSnapshot
    {
        private readonly ChatMessage[] _messages;

        private ConversationSnapshot(
            string title,
            DateTimeOffset createdAt,
            DateTimeOffset updatedAt,
            ChatMessage[] messages)
        {
            Title = title;
            CreatedAt = createdAt;
            UpdatedAt = updatedAt;
            _messages = messages;
        }

        private string Title { get; }

        private DateTimeOffset CreatedAt { get; }

        private DateTimeOffset UpdatedAt { get; }

        public static ConversationSnapshot Capture(Conversation conversation)
        {
            return new ConversationSnapshot(
                conversation.Title,
                conversation.CreatedAt,
                conversation.UpdatedAt,
                conversation.Messages.ToArray());
        }

        public bool Matches(Conversation conversation)
        {
            return string.Equals(
                    Title,
                    conversation.Title,
                    StringComparison.Ordinal)
                && CreatedAt == conversation.CreatedAt
                && UpdatedAt == conversation.UpdatedAt
                && _messages.SequenceEqual(conversation.Messages);
        }
    }
}
