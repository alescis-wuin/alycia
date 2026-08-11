using Alicia.Application.Conversations;
using Alicia.Domain.Conversations;

namespace Alicia.Application.Tests.Conversations;

public sealed class StreamConversationTurnUseCaseTests
{
    [Fact]
    public async Task ExecuteAsyncStreamsChunksAndPersistsCombinedAssistantAfterCompletion()
    {
        DateTimeOffset createdAt = new(2026, 8, 11, 18, 0, 0, TimeSpan.Zero);
        DateTimeOffset responseTime = createdAt.AddMinutes(2);
        InMemoryConversationRepository repository = new();
        Conversation conversation = new(
            ConversationId.New(),
            "Project",
            createdAt,
            createdAt);
        ChatMessage userMessage = new(
            MessageId.New(),
            MessageRole.User,
            "Stream this",
            createdAt.AddMinutes(1));
        conversation.AddMessage(userMessage);
        await repository
            .SaveAsync(conversation, CancellationToken.None)
            .ConfigureAwait(true);
        DeterministicStreamingConversationResponder responder = new(
            "Streamed",
            " ",
            "response");
        StreamConversationTurnUseCase useCase = new(
            repository,
            responder,
            new FixedTimeProvider(responseTime));
        using CancellationTokenSource cancellationSource = new();

        IReadOnlyList<ConversationResponseChunk> chunks = await CollectAsync(
            useCase.ExecuteAsync(
                conversation.Id,
                userMessage.Id,
                cancellationSource.Token)).ConfigureAwait(true);

        Assert.Collection(
            chunks,
            chunk => Assert.Equal("Streamed", chunk.ContentDelta),
            chunk => Assert.Equal(" ", chunk.ContentDelta),
            chunk => Assert.Equal("response", chunk.ContentDelta));
        Assert.Equal(1, responder.CallCount);
        Assert.Equal(cancellationSource.Token, responder.LastCancellationToken);

        ConversationResponseRequest request = Assert.IsType<ConversationResponseRequest>(
            responder.LastRequest);
        Assert.Equal(conversation.Id, request.ConversationId);
        Assert.Collection(
            request.Messages,
            message =>
            {
                Assert.Equal(userMessage.Id, message.Id);
                Assert.Equal("Stream this", message.Content);
            });

        Conversation? persisted = await repository
            .FindAsync(conversation.Id, CancellationToken.None)
            .ConfigureAwait(true);
        Conversation persistedConversation = Assert.IsType<Conversation>(persisted);
        Assert.Collection(
            persistedConversation.Messages,
            message => Assert.Equal(userMessage.Id, message.Id),
            message =>
            {
                Assert.Equal(MessageRole.Assistant, message.Role);
                Assert.Equal("Streamed response", message.Content);
                Assert.Equal(responseTime, message.CreatedAt);
            });
        Assert.Equal(2, repository.SaveCount);
    }

    [Fact]
    public async Task ExecuteAsyncDoesNotPersistPartialAssistantBeforeStreamCompletes()
    {
        DateTimeOffset createdAt = new(2026, 8, 11, 18, 10, 0, TimeSpan.Zero);
        InMemoryConversationRepository repository = new();
        Conversation conversation = new(ConversationId.New(), "Project", createdAt, createdAt);
        ChatMessage userMessage = new(
            MessageId.New(),
            MessageRole.User,
            "Wait",
            createdAt.AddMinutes(1));
        conversation.AddMessage(userMessage);
        await repository.SaveAsync(conversation, CancellationToken.None).ConfigureAwait(true);
        PausingStreamingConversationResponder responder = new("Partial ", "complete");
        StreamConversationTurnUseCase useCase = new(
            repository,
            responder,
            new FixedTimeProvider(createdAt.AddMinutes(2)));

        Task<IReadOnlyList<ConversationResponseChunk>> streamTask = CollectAsync(
            useCase.ExecuteAsync(
                conversation.Id,
                userMessage.Id,
                CancellationToken.None));
        await responder.FirstChunkObserved.ConfigureAwait(true);

        Conversation? duringStream = await repository
            .FindAsync(conversation.Id, CancellationToken.None)
            .ConfigureAwait(true);
        Conversation currentConversation = Assert.IsType<Conversation>(duringStream);
        ChatMessage persistedUser = Assert.Single(currentConversation.Messages);
        Assert.Equal(userMessage.Id, persistedUser.Id);
        Assert.Equal(1, repository.SaveCount);

        responder.Release();
        await streamTask.ConfigureAwait(true);

        Assert.Equal(2, repository.SaveCount);
        Assert.Equal(2, currentConversation.Messages.Count);
        Assert.Equal("Partial complete", currentConversation.Messages[1].Content);
    }

    [Fact]
    public async Task ExecuteAsyncCancellationAfterPartialChunkPersistsNoAssistant()
    {
        DateTimeOffset createdAt = new(2026, 8, 11, 18, 20, 0, TimeSpan.Zero);
        InMemoryConversationRepository repository = new();
        Conversation conversation = new(ConversationId.New(), "Project", createdAt, createdAt);
        ChatMessage userMessage = new(
            MessageId.New(),
            MessageRole.User,
            "Cancel",
            createdAt.AddMinutes(1));
        conversation.AddMessage(userMessage);
        await repository.SaveAsync(conversation, CancellationToken.None).ConfigureAwait(true);
        PausingStreamingConversationResponder responder = new("Partial", "Unreachable");
        StreamConversationTurnUseCase useCase = new(
            repository,
            responder,
            new FixedTimeProvider(createdAt.AddMinutes(2)));
        using CancellationTokenSource cancellationSource = new();

        Task<IReadOnlyList<ConversationResponseChunk>> streamTask = CollectAsync(
            useCase.ExecuteAsync(
                conversation.Id,
                userMessage.Id,
                cancellationSource.Token));
        await responder.FirstChunkObserved.ConfigureAwait(true);
        cancellationSource.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => streamTask).ConfigureAwait(true);

        ChatMessage persistedUser = Assert.Single(conversation.Messages);
        Assert.Equal(userMessage.Id, persistedUser.Id);
        Assert.Equal(1, repository.SaveCount);
    }

    [Fact]
    public async Task ExecuteAsyncResponderFailureAfterPartialChunkPersistsNoAssistant()
    {
        DateTimeOffset createdAt = new(2026, 8, 11, 18, 30, 0, TimeSpan.Zero);
        InMemoryConversationRepository repository = new();
        Conversation conversation = new(ConversationId.New(), "Project", createdAt, createdAt);
        ChatMessage userMessage = new(
            MessageId.New(),
            MessageRole.User,
            "Fail",
            createdAt.AddMinutes(1));
        conversation.AddMessage(userMessage);
        await repository.SaveAsync(conversation, CancellationToken.None).ConfigureAwait(true);
        InvalidOperationException expected = new("Streaming responder failed.");
        FailingAfterFirstStreamingConversationResponder responder = new(
            "Partial",
            expected);
        StreamConversationTurnUseCase useCase = new(
            repository,
            responder,
            new FixedTimeProvider(createdAt.AddMinutes(2)));

        InvalidOperationException actual = await Assert.ThrowsAsync<InvalidOperationException>(
            () => CollectAsync(useCase.ExecuteAsync(
                conversation.Id,
                userMessage.Id,
                CancellationToken.None))).ConfigureAwait(true);

        Assert.Same(expected, actual);
        Assert.Single(conversation.Messages);
        Assert.Equal(1, repository.SaveCount);
    }

    [Fact]
    public async Task ExecuteAsyncRejectsHistoryChangeAfterStreamingWithoutOverwritingIt()
    {
        DateTimeOffset createdAt = new(2026, 8, 11, 18, 40, 0, TimeSpan.Zero);
        InMemoryConversationRepository repository = new();
        Conversation conversation = new(ConversationId.New(), "Project", createdAt, createdAt);
        ChatMessage trigger = new(
            MessageId.New(),
            MessageRole.User,
            "Original",
            createdAt.AddMinutes(1));
        conversation.AddMessage(trigger);
        await repository.SaveAsync(conversation, CancellationToken.None).ConfigureAwait(true);
        PausingStreamingConversationResponder responder = new("Stale ", "response");
        StreamConversationTurnUseCase useCase = new(
            repository,
            responder,
            new FixedTimeProvider(createdAt.AddMinutes(3)));

        Task<IReadOnlyList<ConversationResponseChunk>> streamTask = CollectAsync(
            useCase.ExecuteAsync(
                conversation.Id,
                trigger.Id,
                CancellationToken.None));
        await responder.FirstChunkObserved.ConfigureAwait(true);

        ChatMessage newerUserMessage = new(
            MessageId.New(),
            MessageRole.User,
            "New history",
            createdAt.AddMinutes(2));
        conversation.AddMessage(newerUserMessage);
        await repository.SaveAsync(conversation, CancellationToken.None).ConfigureAwait(true);
        responder.Release();

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => streamTask).ConfigureAwait(true);

        Assert.Contains(
            "changed while Alicia was responding",
            exception.Message,
            StringComparison.Ordinal);
        Assert.Collection(
            conversation.Messages,
            message => Assert.Equal(trigger.Id, message.Id),
            message => Assert.Equal(newerUserMessage.Id, message.Id));
        Assert.Equal(2, repository.SaveCount);
    }

    [Fact]
    public async Task ExecuteAsyncRejectsBlankCompletedStream()
    {
        DateTimeOffset createdAt = new(2026, 8, 11, 18, 50, 0, TimeSpan.Zero);
        InMemoryConversationRepository repository = new();
        Conversation conversation = new(ConversationId.New(), "Project", createdAt, createdAt);
        ChatMessage userMessage = new(
            MessageId.New(),
            MessageRole.User,
            "Blank",
            createdAt.AddMinutes(1));
        conversation.AddMessage(userMessage);
        await repository.SaveAsync(conversation, CancellationToken.None).ConfigureAwait(true);
        StreamConversationTurnUseCase useCase = new(
            repository,
            new DeterministicStreamingConversationResponder(" ", "  "),
            new FixedTimeProvider(createdAt.AddMinutes(2)));

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => CollectAsync(useCase.ExecuteAsync(
                conversation.Id,
                userMessage.Id,
                CancellationToken.None))).ConfigureAwait(true);

        Assert.Contains("no response content", exception.Message, StringComparison.Ordinal);
        Assert.Single(conversation.Messages);
        Assert.Equal(1, repository.SaveCount);
    }

    [Fact]
    public async Task ExecuteAsyncCannotStreamSameTriggerTwice()
    {
        DateTimeOffset createdAt = new(2026, 8, 11, 19, 0, 0, TimeSpan.Zero);
        InMemoryConversationRepository repository = new();
        Conversation conversation = new(ConversationId.New(), "Project", createdAt, createdAt);
        ChatMessage userMessage = new(
            MessageId.New(),
            MessageRole.User,
            "Once",
            createdAt.AddMinutes(1));
        conversation.AddMessage(userMessage);
        await repository.SaveAsync(conversation, CancellationToken.None).ConfigureAwait(true);
        DeterministicStreamingConversationResponder responder = new("Response");
        StreamConversationTurnUseCase useCase = new(
            repository,
            responder,
            new FixedTimeProvider(createdAt.AddMinutes(2)));

        await CollectAsync(useCase.ExecuteAsync(
            conversation.Id,
            userMessage.Id,
            CancellationToken.None)).ConfigureAwait(true);

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => CollectAsync(useCase.ExecuteAsync(
                conversation.Id,
                userMessage.Id,
                CancellationToken.None))).ConfigureAwait(true);

        Assert.Contains("latest unanswered", exception.Message, StringComparison.Ordinal);
        Assert.Equal(1, responder.CallCount);
        Assert.Equal(2, conversation.Messages.Count);
    }

    [Fact]
    public async Task ExecuteAsyncRejectsInvalidConversationOrTriggerBeforeStreaming()
    {
        DeterministicStreamingConversationResponder responder = new("Unused");
        StreamConversationTurnUseCase useCase = new(
            new InMemoryConversationRepository(),
            responder,
            new FixedTimeProvider(new DateTimeOffset(2026, 8, 11, 19, 10, 0, TimeSpan.Zero)));

        await Assert.ThrowsAsync<ArgumentException>(() => CollectAsync(
            useCase.ExecuteAsync(
                default,
                MessageId.New(),
                CancellationToken.None))).ConfigureAwait(true);
        await Assert.ThrowsAsync<ArgumentException>(() => CollectAsync(
            useCase.ExecuteAsync(
                ConversationId.New(),
                default,
                CancellationToken.None))).ConfigureAwait(true);
        await Assert.ThrowsAsync<KeyNotFoundException>(() => CollectAsync(
            useCase.ExecuteAsync(
                ConversationId.New(),
                MessageId.New(),
                CancellationToken.None))).ConfigureAwait(true);

        Assert.Equal(0, responder.CallCount);
    }

    private static async Task<IReadOnlyList<ConversationResponseChunk>> CollectAsync(
        IAsyncEnumerable<ConversationResponseChunk> stream)
    {
        List<ConversationResponseChunk> chunks = [];

        await foreach (ConversationResponseChunk chunk in stream.ConfigureAwait(true))
        {
            chunks.Add(chunk);
        }

        return chunks;
    }
}
