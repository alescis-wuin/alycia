using Alicia.Application.Conversations;
using Alicia.Domain.Conversations;

namespace Alicia.Application.Tests.Conversations;

public sealed class DeleteConversationUseCaseTests
{
    [Fact]
    public async Task ExecuteAsyncDeletesPersistedConversation()
    {
        InMemoryConversationRepository repository = new();
        Conversation conversation = new(
            ConversationId.New(),
            new DateTimeOffset(2026, 8, 8, 6, 0, 0, TimeSpan.Zero));
        await repository.SaveAsync(conversation, CancellationToken.None).ConfigureAwait(true);
        DeleteConversationUseCase useCase = new(repository);

        await useCase.ExecuteAsync(conversation.Id, CancellationToken.None).ConfigureAwait(true);

        Assert.Null(await repository.FindAsync(
            conversation.Id,
            CancellationToken.None).ConfigureAwait(true));
        Assert.Equal(1, repository.DeleteCount);
    }

    [Fact]
    public async Task ExecuteAsyncRejectsUnknownConversation()
    {
        DeleteConversationUseCase useCase = new(new InMemoryConversationRepository());

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            useCase.ExecuteAsync(ConversationId.New(), CancellationToken.None)).ConfigureAwait(true);
    }
}
