using Alicia.Application.Conversations;
using Alicia.Domain.Conversations;

namespace Alicia.Application.Tests.Conversations;

public sealed class RenameConversationUseCaseTests
{
    [Fact]
    public async Task ExecuteAsyncRenamesAndPersistsConversation()
    {
        DateTimeOffset createdAt = new(2026, 8, 8, 6, 0, 0, TimeSpan.Zero);
        DateTimeOffset renamedAt = createdAt.AddMinutes(5);
        InMemoryConversationRepository repository = new();
        Conversation conversation = new(ConversationId.New(), createdAt);
        await repository.SaveAsync(conversation, CancellationToken.None).ConfigureAwait(true);
        RenameConversationUseCase useCase = new(repository, new FixedTimeProvider(renamedAt));

        Conversation renamed = await useCase
            .ExecuteAsync(
                conversation.Id,
                "Project planning",
                CancellationToken.None)
            .ConfigureAwait(true);

        Assert.Same(conversation, renamed);
        Assert.Equal("Project planning", renamed.Title);
        Assert.Equal(renamedAt, renamed.UpdatedAt);
        Assert.Equal(2, repository.SaveCount);
    }

    [Fact]
    public async Task ExecuteAsyncRejectsUnknownConversation()
    {
        RenameConversationUseCase useCase = new(
            new InMemoryConversationRepository(),
            new FixedTimeProvider(new DateTimeOffset(2026, 8, 8, 6, 0, 0, TimeSpan.Zero)));

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            useCase.ExecuteAsync(
                ConversationId.New(),
                "Missing",
                CancellationToken.None)).ConfigureAwait(true);
    }
}
