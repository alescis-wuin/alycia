using System.Collections.ObjectModel;
using Alicia.Domain.Conversations;

namespace Alicia.Application.Conversations;

internal sealed class ConversationTurnSnapshot
{
    private readonly ChatMessage[] _messages;
    private readonly ReadOnlyCollection<MessageRevisionId> _inputMessageRevisionIds;

    private ConversationTurnSnapshot(
        string title,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt,
        ChatMessage[] messages,
        MessageRevisionId triggeringUserMessageRevisionId)
    {
        Title = title;
        CreatedAt = createdAt;
        UpdatedAt = updatedAt;
        _messages = messages;
        TriggeringUserMessageRevisionId = triggeringUserMessageRevisionId;
        _inputMessageRevisionIds = Array.AsReadOnly(
            messages.Select(message => message.RevisionId).ToArray());
    }

    private string Title { get; }

    private DateTimeOffset CreatedAt { get; }

    private DateTimeOffset UpdatedAt { get; }

    public MessageRevisionId TriggeringUserMessageRevisionId { get; }

    public IReadOnlyList<MessageRevisionId> InputMessageRevisionIds => _inputMessageRevisionIds;

    public static ConversationTurnSnapshot Capture(
        Conversation conversation,
        MessageId triggeringUserMessageId)
    {
        ArgumentNullException.ThrowIfNull(conversation);
        ChatMessage trigger = ValidateTrigger(conversation, triggeringUserMessageId);

        return new ConversationTurnSnapshot(
            conversation.Title,
            conversation.CreatedAt,
            conversation.UpdatedAt,
            conversation.Messages.ToArray(),
            trigger.RevisionId);
    }

    public Conversation RequireMatching(Conversation? conversation)
    {
        if (conversation is null || !Matches(conversation))
        {
            throw new InvalidOperationException(
                "Conversation changed while Alicia was responding. Retry from the latest history.");
        }

        return conversation;
    }

    private bool Matches(Conversation conversation)
    {
        return string.Equals(
                Title,
                conversation.Title,
                StringComparison.Ordinal)
            && CreatedAt == conversation.CreatedAt
            && UpdatedAt == conversation.UpdatedAt
            && _messages.SequenceEqual(conversation.Messages);
    }

    private static ChatMessage ValidateTrigger(
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

        return trigger;
    }
}
