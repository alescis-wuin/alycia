using Alicia.Application.Conversations;
using Alicia.Domain.Conversations;

namespace Alicia.Application.Tests.Conversations;

public sealed class ListConversationsUseCaseTests
{
    [Fact]
    public async Task ExecuteAsyncReturnsRepositorySummariesInActivityOrder()
    {
        InMemoryConversationRepository repository = new();
        DateTimeOffset firstCreatedAt = new(2026, 8, 8, 6, 0, 0, TimeSpan.Zero);
        DateTimeOffset secondCreatedAt = firstCreatedAt.AddMinutes(1);
        Conversation first = new(ConversationId.New(), firstCreatedAt);
        Conversation second = new(ConversationId.New(), secondCreatedAt);
        first.AddMessage(new ChatMessage(
            MessageId.New(),
            MessageRole.User,
            "Most recent activity",
            secondCreatedAt.AddMinutes(1)));

        await repository.SaveAsync(first, CancellationToken.None).ConfigureAwait(true);
        await repository.SaveAsync(second, CancellationToken.None).ConfigureAwait(true);
        ListConversationsUseCase useCase = new(repository);

        IReadOnlyList<ConversationSummary> summaries = await useCase
            .ExecuteAsync(CancellationToken.None)
            .ConfigureAwait(true);

        Assert.Collection(
            summaries,
            summary =>
            {
                Assert.Equal(first.Id, summary.Id);
                Assert.Equal(Conversation.DefaultTitle, summary.Title);
                Assert.Equal(1, summary.MessageCount);
            },
            summary =>
            {
                Assert.Equal(second.Id, summary.Id);
                Assert.Equal(0, summary.MessageCount);
            });
    }
}
