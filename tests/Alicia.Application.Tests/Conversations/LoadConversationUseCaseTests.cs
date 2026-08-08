using Alicia.Application.Conversations;
using Alicia.Domain.Conversations;

namespace Alicia.Application.Tests.Conversations;

public sealed class LoadConversationUseCaseTests
{
    [Fact]
    public async Task ExecuteAsyncReturnsPersistedConversation()
    {
        InMemoryConversationRepository repository = new();
        Conversation conversation = new(
            ConversationId.New(),
            new DateTimeOffset(2026, 8, 8, 6, 0, 0, TimeSpan.Zero));
        await repository.SaveAsync(conversation, CancellationToken.None).ConfigureAwait(true);
        LoadConversationUseCase useCase = new(repository);

        Conversation loaded = await useCase
            .ExecuteAsync(conversation.Id, CancellationToken.None)
            .ConfigureAwait(true);

        Assert.Same(conversation, loaded);
    }

    [Fact]
    public async Task ExecuteAsyncRejectsUnknownConversation()
    {
        LoadConversationUseCase useCase = new(new InMemoryConversationRepository());

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            useCase.ExecuteAsync(ConversationId.New(), CancellationToken.None)).ConfigureAwait(true);
    }
}
