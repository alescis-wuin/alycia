using Alicia.Domain.Conversations;

namespace Alicia.Domain.Tests.Conversations;

public sealed class ChatMessageTests
{
    private static readonly DateTimeOffset _timestamp = new(2026, 8, 8, 4, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ConstructorPreservesMessageData()
    {
        MessageId id = new(Guid.Parse("9b5932dd-c8a7-4e92-b985-e83cc06f7afb"));

        ChatMessage message = new(id, MessageRole.User, "Hello Alicia", _timestamp);

        Assert.Equal(id, message.Id);
        Assert.Equal(MessageRole.User, message.Role);
        Assert.Equal("Hello Alicia", message.Content);
        Assert.Equal(_timestamp, message.CreatedAt);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ConstructorRejectsEmptyContent(string content)
    {
        Assert.Throws<ArgumentException>(() =>
            new ChatMessage(MessageId.New(), MessageRole.User, content, _timestamp));
    }

    [Fact]
    public void ConstructorRejectsUndefinedRole()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new ChatMessage(MessageId.New(), (MessageRole)999, "Hello", _timestamp));
    }
}
