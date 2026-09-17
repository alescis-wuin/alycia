using Alicia.Application.Conversations;
using Alicia.Domain.Conversations;

namespace Alicia.Application.Tests.Conversations;

public sealed class AppendMessageUseCaseTests
{
    [Fact]
    public async Task ExecuteAsyncAppendsAndPersistsMessage()
    {
        DateTimeOffset createdAt = new(2026, 8, 8, 4, 0, 0, TimeSpan.Zero);
        DateTimeOffset messageTime = createdAt.AddMinutes(1);
        InMemoryConversationRepository repository = new();
        Conversation conversation = new(ConversationId.New(), createdAt);
        await repository.SaveAsync(conversation, CancellationToken.None).ConfigureAwait(true);
        AppendMessageUseCase useCase = new(repository, new FixedTimeProvider(messageTime));

        ChatMessage message = await useCase.ExecuteAsync(
            conversation.Id,
            MessageRole.User,
            "Hello Alicia",
            CancellationToken.None).ConfigureAwait(true);

        Assert.False(message.Id.IsEmpty);
        Assert.False(message.RevisionId.IsEmpty);
        Assert.Null(message.ParentRevisionId);
        Assert.Matches("^[0-9A-F]{64}$", message.PayloadHash);
        Assert.Equal(MessageRole.User, message.Role);
        Assert.Equal("Hello Alicia", message.Content);
        Assert.Equal(messageTime, message.CreatedAt);
        ChatMessage persistedMessage = Assert.Single(conversation.Messages);
        Assert.Same(message, persistedMessage);
        Assert.Equal(2, repository.SaveCount);
    }

    [Fact]
    public async Task ExecuteAsyncRejectsUnknownConversation()
    {
        InMemoryConversationRepository repository = new();
        AppendMessageUseCase useCase = new(
            repository,
            new FixedTimeProvider(new DateTimeOffset(2026, 8, 8, 4, 0, 0, TimeSpan.Zero)));

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            useCase.ExecuteAsync(
                ConversationId.New(),
                MessageRole.User,
                "Hello Alicia",
                CancellationToken.None)).ConfigureAwait(true);

        Assert.Equal(0, repository.SaveCount);
    }
}
