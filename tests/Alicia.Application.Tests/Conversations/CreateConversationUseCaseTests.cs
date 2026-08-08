using Alicia.Application.Conversations;
using Alicia.Domain.Conversations;

namespace Alicia.Application.Tests.Conversations;

public sealed class CreateConversationUseCaseTests
{
    [Fact]
    public async Task ExecuteAsyncCreatesAndPersistsConversation()
    {
        DateTimeOffset now = new(2026, 8, 8, 4, 15, 0, TimeSpan.Zero);
        InMemoryConversationRepository repository = new();
        CreateConversationUseCase useCase = new(repository, new FixedTimeProvider(now));

        Conversation conversation = await useCase.ExecuteAsync(CancellationToken.None).ConfigureAwait(true);
        Conversation? persisted = await repository.FindAsync(
            conversation.Id,
            CancellationToken.None).ConfigureAwait(true);

        Assert.False(conversation.Id.IsEmpty);
        Assert.Equal(now, conversation.CreatedAt);
        Assert.Empty(conversation.Messages);
        Assert.Same(conversation, persisted);
        Assert.Equal(1, repository.SaveCount);
    }
}
