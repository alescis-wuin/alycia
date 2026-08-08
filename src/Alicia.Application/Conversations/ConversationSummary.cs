using Alicia.Domain.Conversations;

namespace Alicia.Application.Conversations;

public sealed record ConversationSummary(
    ConversationId Id,
    string Title,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    int MessageCount)
{
    public static ConversationSummary FromConversation(Conversation conversation)
    {
        ArgumentNullException.ThrowIfNull(conversation);

        return new ConversationSummary(
            conversation.Id,
            conversation.Title,
            conversation.CreatedAt,
            conversation.UpdatedAt,
            conversation.Messages.Count);
    }
}
