using Alicia.Domain.Conversations;

namespace Alicia.Domain.Tests.Conversations;

public sealed class ConversationTests
{
    private static readonly DateTimeOffset _createdAt = new(2026, 8, 8, 4, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ConstructorPreservesDefaultMetadata()
    {
        Conversation conversation = new(ConversationId.New(), _createdAt);

        Assert.Equal(Conversation.DefaultTitle, conversation.Title);
        Assert.Equal(_createdAt, conversation.CreatedAt);
        Assert.Equal(_createdAt, conversation.UpdatedAt);
    }

    [Fact]
    public void ConstructorPreservesExplicitMetadata()
    {
        DateTimeOffset updatedAt = _createdAt.AddMinutes(2);

        Conversation conversation = new(
            ConversationId.New(),
            "Architecture",
            _createdAt,
            updatedAt);

        Assert.Equal("Architecture", conversation.Title);
        Assert.Equal(updatedAt, conversation.UpdatedAt);
    }

    [Fact]
    public void ConstructorRejectsInvalidTitle()
    {
        Assert.Throws<ArgumentException>(() =>
            new Conversation(
                ConversationId.New(),
                "   ",
                _createdAt,
                _createdAt));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new Conversation(
                ConversationId.New(),
                new string('a', Conversation.MaxTitleLength + 1),
                _createdAt,
                _createdAt));

        Assert.Throws<ArgumentException>(() =>
            new Conversation(
                ConversationId.New(),
                "Line\nbreak",
                _createdAt,
                _createdAt));
    }

    [Fact]
    public void ConstructorRejectsUpdateBeforeCreation()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new Conversation(
                ConversationId.New(),
                "Title",
                _createdAt,
                _createdAt.AddTicks(-1)));
    }

    [Fact]
    public void RenameUpdatesTitleAndActivity()
    {
        Conversation conversation = new(ConversationId.New(), _createdAt);
        DateTimeOffset renamedAt = _createdAt.AddMinutes(1);

        conversation.Rename("  Project planning  ", renamedAt);

        Assert.Equal("Project planning", conversation.Title);
        Assert.Equal(renamedAt, conversation.UpdatedAt);
    }

    [Fact]
    public void RenameRejectsPastActivityTime()
    {
        Conversation conversation = new(
            ConversationId.New(),
            "Current",
            _createdAt,
            _createdAt.AddMinutes(2));

        Assert.Throws<InvalidOperationException>(() =>
            conversation.Rename("Past", _createdAt.AddMinutes(1)));
    }

    [Fact]
    public void AddMessagePreservesInsertionOrderAndAdvancesActivity()
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
        Assert.Equal(second.CreatedAt, conversation.UpdatedAt);
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
    public void AddMessageRejectsDuplicateRevisionIdentifier()
    {
        Conversation conversation = new(ConversationId.New(), _createdAt);
        MessageRevisionId revisionId = MessageRevisionId.New();

        conversation.AddMessage(new ChatMessage(
            MessageId.New(),
            revisionId,
            parentRevisionId: null,
            MessageRole.User,
            "First",
            _createdAt));

        Assert.Throws<InvalidOperationException>(() =>
            conversation.AddMessage(new ChatMessage(
                MessageId.New(),
                revisionId,
                parentRevisionId: null,
                MessageRole.Assistant,
                "Duplicate revision",
                _createdAt)));
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
