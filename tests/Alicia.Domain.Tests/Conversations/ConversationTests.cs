using Alicia.Domain.Conversations;

namespace Alicia.Domain.Tests.Conversations;

public sealed class ConversationTests
{
    private static readonly DateTimeOffset _createdAt = new(2026, 8, 8, 4, 0, 0, TimeSpan.Zero);

    [Fact]
    public void AddMessagePreservesInsertionOrder()
    {
        Conversation conversation = new(ConversationId.New(), _createdAt);
        ChatMessage first = new(MessageId.New(), MessageRole.User, "First", _createdAt);
        ChatMessage second = new(MessageId.New(), MessageRole.Assistant, "Second", _createdAt.AddSeconds(1));

        conversation.AddMessage(first);
        conversation.AddMessage(second);

        Assert.Collection(
            conversation.Messages,
            message => Assert.Same(first, message),
            message => Assert.Same(second, message));
    }

    [Fact]
    public void AddMessageRejectsDuplicateIdentifier()
    {
        Conversation conversation = new(ConversationId.New(), _createdAt);
        MessageId messageId = MessageId.New();

        conversation.AddMessage(new ChatMessage(messageId, MessageRole.User, "First", _createdAt));

        Assert.Throws<InvalidOperationException>(() =>
            conversation.AddMessage(new ChatMessage(messageId, MessageRole.Assistant, "Duplicate", _createdAt)));
    }

    [Fact]
    public void AddMessageRejectsMessagePredatingConversation()
    {
        Conversation conversation = new(ConversationId.New(), _createdAt);
        ChatMessage message = new(
            MessageId.New(),
            MessageRole.System,
            "System context",
            _createdAt.AddTicks(-1));

        Assert.Throws<InvalidOperationException>(() => conversation.AddMessage(message));
    }
}
