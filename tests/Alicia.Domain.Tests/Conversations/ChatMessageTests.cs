using Alicia.Domain.Conversations;

namespace Alicia.Domain.Tests.Conversations;

public sealed class ChatMessageTests
{
    private static readonly DateTimeOffset _timestamp = new(2026, 8, 8, 4, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ConstructorPreservesMessageRevisionData()
    {
        MessageId id = new(Guid.Parse("9b5932dd-c8a7-4e92-b985-e83cc06f7afb"));
        MessageRevisionId revisionId = new(Guid.Parse("293628d6-bbb7-41b0-91e2-e131617d4d4f"));
        MessageRevisionId parentRevisionId = new(Guid.Parse("ab460a83-890e-4ce2-9747-fbbf3b9b3f69"));

        ChatMessage message = new(
            id,
            revisionId,
            parentRevisionId,
            MessageRole.User,
            "Hello Alicia",
            _timestamp);

        Assert.Equal(id, message.Id);
        Assert.Equal(revisionId, message.RevisionId);
        Assert.Equal(parentRevisionId, message.ParentRevisionId);
        Assert.Equal(MessageRole.User, message.Role);
        Assert.Equal("Hello Alicia", message.Content);
        Assert.Equal(_timestamp, message.CreatedAt);
        Assert.Matches("^[0-9A-F]{64}$", message.PayloadHash);
    }

    [Fact]
    public void CompatibilityConstructorCreatesRootRevision()
    {
        ChatMessage message = new(
            MessageId.New(),
            MessageRole.User,
            "Hello Alicia",
            _timestamp);

        Assert.False(message.RevisionId.IsEmpty);
        Assert.Null(message.ParentRevisionId);
    }

    [Fact]
    public void PayloadHashIsDeterministicForSameMessagePayload()
    {
        MessageId messageId = new(Guid.Parse("13ec8e3e-f2d7-4f3d-b6fb-6eb0cc76ae95"));
        ChatMessage first = new(
            messageId,
            new MessageRevisionId(Guid.Parse("59c3f188-1d4d-490b-828d-ea891908e539")),
            parentRevisionId: null,
            MessageRole.Assistant,
            "Stable payload",
            _timestamp);
        ChatMessage second = new(
            messageId,
            new MessageRevisionId(Guid.Parse("fd35fcfa-945f-4ec4-bd30-1bac8ca9eb1f")),
            first.RevisionId,
            MessageRole.Assistant,
            "Stable payload",
            _timestamp);
        ChatMessage changed = new(
            messageId,
            new MessageRevisionId(Guid.Parse("07648ad5-14f9-4895-af37-4fd0f9ec77ea")),
            second.RevisionId,
            MessageRole.Assistant,
            "Changed payload",
            _timestamp);

        Assert.Equal(first.PayloadHash, second.PayloadHash);
        Assert.NotEqual(first.PayloadHash, changed.PayloadHash);
    }

    [Fact]
    public void ConstructorRejectsInvalidRevisionIdentifiers()
    {
        MessageRevisionId revisionId = MessageRevisionId.New();

        Assert.Throws<ArgumentException>(() =>
            new ChatMessage(
                MessageId.New(),
                default,
                parentRevisionId: null,
                MessageRole.User,
                "Hello",
                _timestamp));
        Assert.Throws<ArgumentException>(() =>
            new ChatMessage(
                MessageId.New(),
                revisionId,
                default(MessageRevisionId),
                MessageRole.User,
                "Hello",
                _timestamp));
        Assert.Throws<ArgumentException>(() =>
            new ChatMessage(
                MessageId.New(),
                revisionId,
                revisionId,
                MessageRole.User,
                "Hello",
                _timestamp));
    }


    [Fact]
    public void AssistantRevisionCanReferenceGenerationSnapshotWithoutChangingPayloadHash()
    {
        MessageId messageId = MessageId.New();
        MessageRevisionId revisionId = MessageRevisionId.New();
        GenerationSnapshotId snapshotId = GenerationSnapshotId.New();
        ChatMessage withoutSnapshot = new(
            messageId,
            revisionId,
            parentRevisionId: null,
            MessageRole.Assistant,
            "Generated answer",
            _timestamp);
        ChatMessage withSnapshot = new(
            messageId,
            revisionId,
            parentRevisionId: null,
            MessageRole.Assistant,
            "Generated answer",
            _timestamp,
            snapshotId);

        Assert.Equal(snapshotId, withSnapshot.GenerationSnapshotId);
        Assert.Equal(withoutSnapshot.PayloadHash, withSnapshot.PayloadHash);
    }

    [Fact]
    public void NonAssistantRevisionCannotReferenceGenerationSnapshot()
    {
        GenerationSnapshotId snapshotId = GenerationSnapshotId.New();

        Assert.Throws<ArgumentException>(() => new ChatMessage(
            MessageId.New(),
            MessageRevisionId.New(),
            parentRevisionId: null,
            MessageRole.User,
            "User input",
            _timestamp,
            snapshotId));
        Assert.Throws<ArgumentException>(() => new ChatMessage(
            MessageId.New(),
            MessageRevisionId.New(),
            parentRevisionId: null,
            MessageRole.Assistant,
            "Assistant output",
            _timestamp,
            default(GenerationSnapshotId)));
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
