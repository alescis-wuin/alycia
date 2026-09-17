using Alicia.Application.Conversations;
using Alicia.Domain.Conversations;

namespace Alicia.Application.Tests.Conversations;

public sealed class CompleteConversationTurnUseCaseTests
{
    [Fact]
    public async Task ExecuteAsyncPersistsAssistantForLatestUserMessage()
    {
        DateTimeOffset createdAt = new(2026, 8, 11, 14, 0, 0, TimeSpan.Zero);
        DateTimeOffset assistantCreatedAt = createdAt.AddMinutes(3);
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
        ChatMessage userMessage = new(
            MessageId.New(),
            MessageRole.User,
            "Summarize this.",
            createdAt.AddMinutes(2));
        conversation.AddMessage(userMessage);
        await repository.SaveAsync(
            conversation,
            CancellationToken.None).ConfigureAwait(true);

        DeterministicConversationResponder responder =
            new("Deterministic assistant response");
        CompleteConversationTurnUseCase useCase = new(
            repository,
            responder,
            new FixedTimeProvider(assistantCreatedAt));
        using CancellationTokenSource cancellationSource = new();

        ChatMessage assistantMessage = await useCase.ExecuteAsync(
            conversation.Id,
            userMessage.Id,
            cancellationSource.Token).ConfigureAwait(true);

        Assert.Equal(MessageRole.Assistant, assistantMessage.Role);
        Assert.False(assistantMessage.RevisionId.IsEmpty);
        Assert.Null(assistantMessage.ParentRevisionId);
        Assert.Matches("^[0-9A-F]{64}$", assistantMessage.PayloadHash);
        Assert.Equal("Deterministic assistant response", assistantMessage.Content);
        Assert.Equal(assistantCreatedAt, assistantMessage.CreatedAt);
        Assert.Equal(2, repository.SaveCount);
        Assert.Equal(1, responder.CallCount);
        Assert.Equal(cancellationSource.Token, responder.LastCancellationToken);

        ConversationResponseRequest request =
            Assert.IsType<ConversationResponseRequest>(responder.LastRequest);
        Assert.Equal(conversation.Id, request.ConversationId);
        Assert.Equal("Project", request.Title);
        Assert.Collection(
            request.Messages,
            message => Assert.Equal(MessageRole.System, message.Role),
            message =>
            {
                Assert.Equal(userMessage.Id, message.Id);
                Assert.Equal(MessageRole.User, message.Role);
            });

        Assert.Collection(
            conversation.Messages,
            message => Assert.Equal(MessageRole.System, message.Role),
            message => Assert.Equal(userMessage.Id, message.Id),
            message => Assert.Same(assistantMessage, message));
    }

    [Fact]
    public async Task ExecuteAsyncKeepsUserMessageWhenResponderFails()
    {
        DateTimeOffset createdAt = new(2026, 8, 11, 14, 10, 0, TimeSpan.Zero);
        InMemoryConversationRepository repository = new();
        Conversation conversation = new(ConversationId.New(), createdAt);
        ChatMessage userMessage = new(
            MessageId.New(),
            MessageRole.User,
            "Keep me",
            createdAt.AddMinutes(1));
        conversation.AddMessage(userMessage);
        await repository.SaveAsync(
            conversation,
            CancellationToken.None).ConfigureAwait(true);
        InvalidOperationException expectedException =
            new("Responder unavailable.");
        CompleteConversationTurnUseCase useCase = new(
            repository,
            new FailingConversationResponder(expectedException),
            new FixedTimeProvider(createdAt.AddMinutes(2)));

        InvalidOperationException actualException =
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                useCase.ExecuteAsync(
                    conversation.Id,
                    userMessage.Id,
                    CancellationToken.None)).ConfigureAwait(true);

        Assert.Same(expectedException, actualException);
        Assert.Equal(1, repository.SaveCount);
        ChatMessage persistedMessage = Assert.Single(conversation.Messages);
        Assert.Same(userMessage, persistedMessage);
    }

    [Fact]
    public async Task ExecuteAsyncCancellationAddsNoAssistantMessage()
    {
        DateTimeOffset createdAt = new(2026, 8, 11, 14, 20, 0, TimeSpan.Zero);
        InMemoryConversationRepository repository = new();
        Conversation conversation = new(ConversationId.New(), createdAt);
        ChatMessage userMessage = new(
            MessageId.New(),
            MessageRole.User,
            "Cancel this response",
            createdAt.AddMinutes(1));
        conversation.AddMessage(userMessage);
        await repository.SaveAsync(
            conversation,
            CancellationToken.None).ConfigureAwait(true);
        using CancellationTokenSource cancellationSource = new();
        DelegateConversationResponder responder = new((_, cancellationToken) =>
        {
            cancellationSource.Cancel();
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new ConversationResponse("Unused"));
        });
        CompleteConversationTurnUseCase useCase = new(
            repository,
            responder,
            new FixedTimeProvider(createdAt.AddMinutes(2)));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            useCase.ExecuteAsync(
                conversation.Id,
                userMessage.Id,
                cancellationSource.Token)).ConfigureAwait(true);

        Assert.Equal(1, responder.CallCount);
        Assert.Equal(1, repository.SaveCount);
        ChatMessage persistedMessage = Assert.Single(conversation.Messages);
        Assert.Same(userMessage, persistedMessage);
    }

    [Fact]
    public async Task ExecuteAsyncRejectsHistoryChangeWithoutOverwritingIt()
    {
        DateTimeOffset createdAt = new(2026, 8, 11, 14, 30, 0, TimeSpan.Zero);
        InMemoryConversationRepository repository = new();
        Conversation conversation = new(ConversationId.New(), createdAt);
        ChatMessage userMessage = new(
            MessageId.New(),
            MessageRole.User,
            "Original trigger",
            createdAt.AddMinutes(1));
        conversation.AddMessage(userMessage);
        await repository.SaveAsync(
            conversation,
            CancellationToken.None).ConfigureAwait(true);

        ChatMessage laterMessage = new(
            MessageId.New(),
            MessageRole.User,
            "Concurrent message",
            createdAt.AddMinutes(2));
        DelegateConversationResponder responder = new(async (_, cancellationToken) =>
        {
            conversation.AddMessage(laterMessage);
            await repository.SaveAsync(
                conversation,
                cancellationToken).ConfigureAwait(false);
            return new ConversationResponse("Stale response");
        });
        CompleteConversationTurnUseCase useCase = new(
            repository,
            responder,
            new FixedTimeProvider(createdAt.AddMinutes(3)));

        InvalidOperationException exception =
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                useCase.ExecuteAsync(
                    conversation.Id,
                    userMessage.Id,
                    CancellationToken.None)).ConfigureAwait(true);

        Assert.Contains(
            "changed while Alicia was responding",
            exception.Message,
            StringComparison.Ordinal);
        Assert.Equal(2, repository.SaveCount);
        Assert.Collection(
            conversation.Messages,
            message => Assert.Same(userMessage, message),
            message => Assert.Same(laterMessage, message));
    }

    [Fact]
    public async Task ExecuteAsyncCannotCompleteSameTriggerTwice()
    {
        DateTimeOffset createdAt = new(2026, 8, 11, 14, 40, 0, TimeSpan.Zero);
        InMemoryConversationRepository repository = new();
        Conversation conversation = new(ConversationId.New(), createdAt);
        ChatMessage userMessage = new(
            MessageId.New(),
            MessageRole.User,
            "Only one reply",
            createdAt.AddMinutes(1));
        conversation.AddMessage(userMessage);
        await repository.SaveAsync(
            conversation,
            CancellationToken.None).ConfigureAwait(true);
        DeterministicConversationResponder responder = new("Assistant reply");
        CompleteConversationTurnUseCase useCase = new(
            repository,
            responder,
            new FixedTimeProvider(createdAt.AddMinutes(2)));

        await useCase.ExecuteAsync(
            conversation.Id,
            userMessage.Id,
            CancellationToken.None).ConfigureAwait(true);

        InvalidOperationException exception =
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                useCase.ExecuteAsync(
                    conversation.Id,
                    userMessage.Id,
                    CancellationToken.None)).ConfigureAwait(true);

        Assert.Contains(
            "latest unanswered message",
            exception.Message,
            StringComparison.Ordinal);
        Assert.Equal(1, responder.CallCount);
        Assert.Equal(2, repository.SaveCount);
        Assert.Equal(2, conversation.Messages.Count);
    }

    [Fact]
    public async Task ExecuteAsyncRejectsUnknownTrigger()
    {
        DateTimeOffset createdAt = new(2026, 8, 11, 14, 50, 0, TimeSpan.Zero);
        InMemoryConversationRepository repository = new();
        Conversation conversation = new(ConversationId.New(), createdAt);
        conversation.AddMessage(new ChatMessage(
            MessageId.New(),
            MessageRole.User,
            "Known message",
            createdAt.AddMinutes(1)));
        await repository.SaveAsync(
            conversation,
            CancellationToken.None).ConfigureAwait(true);
        DeterministicConversationResponder responder = new("Unused");
        CompleteConversationTurnUseCase useCase = new(
            repository,
            responder,
            new FixedTimeProvider(createdAt.AddMinutes(2)));

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            useCase.ExecuteAsync(
                conversation.Id,
                MessageId.New(),
                CancellationToken.None)).ConfigureAwait(true);

        Assert.Equal(0, responder.CallCount);
        Assert.Equal(1, repository.SaveCount);
    }

    [Fact]
    public async Task ExecuteAsyncRejectsNonUserTrigger()
    {
        DateTimeOffset createdAt = new(2026, 8, 11, 15, 0, 0, TimeSpan.Zero);
        InMemoryConversationRepository repository = new();
        Conversation conversation = new(ConversationId.New(), createdAt);
        ChatMessage systemMessage = new(
            MessageId.New(),
            MessageRole.System,
            "System context",
            createdAt.AddMinutes(1));
        conversation.AddMessage(systemMessage);
        await repository.SaveAsync(
            conversation,
            CancellationToken.None).ConfigureAwait(true);
        DeterministicConversationResponder responder = new("Unused");
        CompleteConversationTurnUseCase useCase = new(
            repository,
            responder,
            new FixedTimeProvider(createdAt.AddMinutes(2)));

        InvalidOperationException exception =
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                useCase.ExecuteAsync(
                    conversation.Id,
                    systemMessage.Id,
                    CancellationToken.None)).ConfigureAwait(true);

        Assert.Contains(
            "Only a user message",
            exception.Message,
            StringComparison.Ordinal);
        Assert.Equal(0, responder.CallCount);
    }

    [Fact]
    public async Task ExecuteAsyncRejectsOlderUserTrigger()
    {
        DateTimeOffset createdAt = new(2026, 8, 11, 15, 10, 0, TimeSpan.Zero);
        InMemoryConversationRepository repository = new();
        Conversation conversation = new(ConversationId.New(), createdAt);
        ChatMessage olderUserMessage = new(
            MessageId.New(),
            MessageRole.User,
            "Older",
            createdAt.AddMinutes(1));
        conversation.AddMessage(olderUserMessage);
        conversation.AddMessage(new ChatMessage(
            MessageId.New(),
            MessageRole.User,
            "Newer",
            createdAt.AddMinutes(2)));
        await repository.SaveAsync(
            conversation,
            CancellationToken.None).ConfigureAwait(true);
        DeterministicConversationResponder responder = new("Unused");
        CompleteConversationTurnUseCase useCase = new(
            repository,
            responder,
            new FixedTimeProvider(createdAt.AddMinutes(3)));

        InvalidOperationException exception =
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                useCase.ExecuteAsync(
                    conversation.Id,
                    olderUserMessage.Id,
                    CancellationToken.None)).ConfigureAwait(true);

        Assert.Contains(
            "latest unanswered message",
            exception.Message,
            StringComparison.Ordinal);
        Assert.Equal(0, responder.CallCount);
    }

    [Fact]
    public async Task ExecuteAsyncRejectsMissingResponderOutput()
    {
        DateTimeOffset createdAt = new(2026, 8, 11, 15, 20, 0, TimeSpan.Zero);
        InMemoryConversationRepository repository = new();
        Conversation conversation = new(ConversationId.New(), createdAt);
        ChatMessage userMessage = new(
            MessageId.New(),
            MessageRole.User,
            "Missing output",
            createdAt.AddMinutes(1));
        conversation.AddMessage(userMessage);
        await repository.SaveAsync(
            conversation,
            CancellationToken.None).ConfigureAwait(true);
        CompleteConversationTurnUseCase useCase = new(
            repository,
            new NullConversationResponder(),
            new FixedTimeProvider(createdAt.AddMinutes(2)));

        InvalidOperationException exception =
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                useCase.ExecuteAsync(
                    conversation.Id,
                    userMessage.Id,
                    CancellationToken.None)).ConfigureAwait(true);

        Assert.Contains(
            "returned no response",
            exception.Message,
            StringComparison.Ordinal);
        Assert.Equal(1, repository.SaveCount);
        Assert.Single(conversation.Messages);
    }

    [Fact]
    public async Task ExecuteAsyncRejectsUnknownConversation()
    {
        DeterministicConversationResponder responder = new("Unused");
        CompleteConversationTurnUseCase useCase = new(
            new InMemoryConversationRepository(),
            responder,
            new FixedTimeProvider(
                new DateTimeOffset(2026, 8, 11, 15, 30, 0, TimeSpan.Zero)));

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            useCase.ExecuteAsync(
                ConversationId.New(),
                MessageId.New(),
                CancellationToken.None)).ConfigureAwait(true);

        Assert.Equal(0, responder.CallCount);
    }

    [Fact]
    public async Task ExecuteAsyncRejectsEmptyIdentifiers()
    {
        DeterministicConversationResponder responder = new("Unused");
        CompleteConversationTurnUseCase useCase = new(
            new InMemoryConversationRepository(),
            responder,
            new FixedTimeProvider(
                new DateTimeOffset(2026, 8, 11, 15, 40, 0, TimeSpan.Zero)));

        await Assert.ThrowsAsync<ArgumentException>(() =>
            useCase.ExecuteAsync(
                default,
                MessageId.New(),
                CancellationToken.None)).ConfigureAwait(true);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            useCase.ExecuteAsync(
                ConversationId.New(),
                default,
                CancellationToken.None)).ConfigureAwait(true);

        Assert.Equal(0, responder.CallCount);
    }
}
