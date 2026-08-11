using Alicia.Application.Conversations;
using Alicia.Domain.Conversations;

namespace Alicia.Application.Tests.Conversations;

public sealed class GenerateConversationResponseUseCaseTests
{
    [Fact]
    public async Task ExecuteAsyncBuildsSnapshotAndReturnsResponderOutput()
    {
        DateTimeOffset createdAt =
            new(2026, 8, 11, 0, 10, 0, TimeSpan.Zero);
        InMemoryConversationRepository repository = new();
        Conversation conversation = new(
            ConversationId.New(),
            "Project",
            createdAt,
            createdAt);
        conversation.AddMessage(new ChatMessage(
            MessageId.New(),
            MessageRole.System,
            "Be concise.",
            createdAt.AddMinutes(1)));
        conversation.AddMessage(new ChatMessage(
            MessageId.New(),
            MessageRole.User,
            "Summarize this.",
            createdAt.AddMinutes(2)));
        await repository
            .SaveAsync(conversation, CancellationToken.None)
            .ConfigureAwait(true);

        DeterministicConversationResponder responder =
            new("Deterministic response");
        GenerateConversationResponseUseCase useCase =
            new(repository, responder);
        using CancellationTokenSource cancellationSource =
            new();

        ConversationResponse response = await useCase
            .ExecuteAsync(
                conversation.Id,
                cancellationSource.Token)
            .ConfigureAwait(true);

        Assert.Equal("Deterministic response", response.Content);
        Assert.Equal(1, responder.CallCount);

        ConversationResponseRequest request =
            Assert.IsType<ConversationResponseRequest>(
                responder.LastRequest);
        Assert.Equal(conversation.Id, request.ConversationId);
        Assert.Equal("Project", request.Title);
        Assert.Collection(
            request.Messages,
            message =>
            {
                Assert.Equal(MessageRole.System, message.Role);
                Assert.Equal("Be concise.", message.Content);
            },
            message =>
            {
                Assert.Equal(MessageRole.User, message.Role);
                Assert.Equal("Summarize this.", message.Content);
            });
        Assert.Equal(
            cancellationSource.Token,
            responder.LastCancellationToken);

        conversation.AddMessage(new ChatMessage(
            MessageId.New(),
            MessageRole.User,
            "Later message",
            createdAt.AddMinutes(3)));

        Assert.Equal(2, request.Messages.Count);
        Assert.Equal(1, repository.SaveCount);
    }

    [Fact]
    public async Task ExecuteAsyncRejectsUnknownConversationWithoutCallingResponder()
    {
        InMemoryConversationRepository repository = new();
        DeterministicConversationResponder responder =
            new("Unused");
        GenerateConversationResponseUseCase useCase =
            new(repository, responder);

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            useCase.ExecuteAsync(
                ConversationId.New(),
                CancellationToken.None)).ConfigureAwait(true);

        Assert.Equal(0, responder.CallCount);
    }

    [Fact]
    public async Task ExecuteAsyncRejectsEmptyConversationIdentifier()
    {
        DeterministicConversationResponder responder =
            new("Unused");
        GenerateConversationResponseUseCase useCase =
            new(new InMemoryConversationRepository(), responder);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            useCase.ExecuteAsync(
                default,
                CancellationToken.None)).ConfigureAwait(true);

        Assert.Equal(0, responder.CallCount);
    }

    [Fact]
    public async Task ExecuteAsyncPropagatesResponderFailure()
    {
        DateTimeOffset createdAt =
            new(2026, 8, 11, 0, 15, 0, TimeSpan.Zero);
        InMemoryConversationRepository repository = new();
        Conversation conversation = new(
            ConversationId.New(),
            createdAt);
        await repository
            .SaveAsync(conversation, CancellationToken.None)
            .ConfigureAwait(true);
        InvalidOperationException expectedException =
            new("Responder unavailable.");
        GenerateConversationResponseUseCase useCase =
            new(
                repository,
                new FailingConversationResponder(expectedException));

        InvalidOperationException actualException =
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                useCase.ExecuteAsync(
                    conversation.Id,
                    CancellationToken.None)).ConfigureAwait(true);

        Assert.Same(expectedException, actualException);
        Assert.Equal(1, repository.SaveCount);
    }

    [Fact]
    public async Task ExecuteAsyncRejectsMissingResponderOutput()
    {
        DateTimeOffset createdAt =
            new(2026, 8, 11, 0, 20, 0, TimeSpan.Zero);
        InMemoryConversationRepository repository = new();
        Conversation conversation = new(
            ConversationId.New(),
            createdAt);
        await repository
            .SaveAsync(conversation, CancellationToken.None)
            .ConfigureAwait(true);
        GenerateConversationResponseUseCase useCase =
            new(repository, new NullConversationResponder());

        InvalidOperationException exception =
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                useCase.ExecuteAsync(
                    conversation.Id,
                    CancellationToken.None)).ConfigureAwait(true);

        Assert.Contains(
            "returned no response",
            exception.Message,
            StringComparison.Ordinal);
        Assert.Equal(1, repository.SaveCount);
    }
}
