using System.Collections.ObjectModel;
using Alicia.Domain.Conversations;

namespace Alicia.Application.Conversations;

public sealed class ConversationResponseRequest
{
    private readonly ReadOnlyCollection<ChatMessage> _messages;

    public ConversationResponseRequest(
        ConversationId conversationId,
        string title,
        IEnumerable<ChatMessage> messages)
    {
        if (conversationId.IsEmpty)
        {
            throw new ArgumentException(
                "Conversation identifier cannot be empty.",
                nameof(conversationId));
        }

        if (string.IsNullOrWhiteSpace(title))
        {
            throw new ArgumentException(
                "Conversation title cannot be empty or whitespace.",
                nameof(title));
        }

        ArgumentNullException.ThrowIfNull(messages);

        ChatMessage[] messageSnapshot = messages.ToArray();

        ConversationId = conversationId;
        Title = title;
        _messages = Array.AsReadOnly(messageSnapshot);
    }

    public ConversationId ConversationId { get; }

    public string Title { get; }

    public IReadOnlyList<ChatMessage> Messages => _messages;

    public static ConversationResponseRequest FromConversation(Conversation conversation)
    {
        ArgumentNullException.ThrowIfNull(conversation);

        return new ConversationResponseRequest(
            conversation.Id,
            conversation.Title,
            conversation.Messages);
    }
}
