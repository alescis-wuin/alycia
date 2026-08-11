using Alicia.Application.Conversations;
using Alicia.Domain.Conversations;

namespace Alicia.Application.Tests.Conversations;

public sealed class ConversationResponseContractTests
{
    [Fact]
    public void ResponseRejectsBlankContent()
    {
        Assert.Throws<ArgumentException>(() => new ConversationResponse("   "));
    }

    [Fact]
    public void StreamingChunkRejectsEmptyButPreservesWhitespaceDelta()
    {
        Assert.Throws<ArgumentException>(() => new ConversationResponseChunk(string.Empty));

        ConversationResponseChunk chunk = new(" ");

        Assert.Equal(" ", chunk.ContentDelta);
    }

    [Fact]
    public void ResponsePreservesResponderContentExactly()
    {
        const string content = "  First line\nSecond line  ";

        ConversationResponse response = new(content);

        Assert.Equal(content, response.Content);
    }

    [Fact]
    public void RequestSnapshotsConversationMessages()
    {
        DateTimeOffset createdAt =
            new(2026, 8, 11, 0, 0, 0, TimeSpan.Zero);
        Conversation conversation = new(
            ConversationId.New(),
            "Project",
            createdAt,
            createdAt);
        conversation.AddMessage(new ChatMessage(
            MessageId.New(),
            MessageRole.User,
            "First",
            createdAt.AddMinutes(1)));

        ConversationResponseRequest request =
            ConversationResponseRequest.FromConversation(conversation);

        conversation.AddMessage(new ChatMessage(
            MessageId.New(),
            MessageRole.Assistant,
            "Second",
            createdAt.AddMinutes(2)));

        ChatMessage message = Assert.Single(request.Messages);
        Assert.Equal("First", message.Content);
        Assert.Equal(conversation.Id, request.ConversationId);
        Assert.Equal("Project", request.Title);
    }

    [Fact]
    public void RequestRejectsInvalidIdentityTitleAndMessageSource()
    {
        ConversationId conversationId = ConversationId.New();

        Assert.Throws<ArgumentException>(() =>
            new ConversationResponseRequest(default, "Project", Array.Empty<ChatMessage>()));
        Assert.Throws<ArgumentException>(() =>
            new ConversationResponseRequest(conversationId, "   ", Array.Empty<ChatMessage>()));
        Assert.Throws<ArgumentNullException>(() =>
            new ConversationResponseRequest(
                conversationId,
                "Project",
                null!));
    }
}
