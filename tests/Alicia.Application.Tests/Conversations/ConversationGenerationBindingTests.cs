using Alicia.Application.Conversations;
using Alicia.Application.Generations;
using Alicia.Application.Providers;
using Alicia.Domain.Conversations;

namespace Alicia.Application.Tests.Conversations;

public sealed class ConversationGenerationBindingTests
{
    [Fact]
    public async Task StreamingTurnCarriesAndPersistsExactGenerationSnapshot()
    {
        DateTimeOffset createdAt = new(2026, 9, 16, 12, 0, 0, TimeSpan.Zero);
        Conversation conversation = new(
            ConversationId.New(),
            "Bound stream",
            createdAt,
            createdAt);
        ChatMessage userMessage = new(
            MessageId.New(),
            MessageRole.User,
            "Use my profile",
            createdAt.AddMinutes(1));
        conversation.AddMessage(userMessage);
        InMemoryConversationRepository repository = new();
        await repository.SaveAsync(
            conversation,
            TestContext.Current.CancellationToken).ConfigureAwait(true);
        GenerationSnapshot generationSnapshot = CreateSnapshot(
            conversation.Id,
            userMessage,
            createdAt.AddMinutes(1));
        CapturingGenerationResolver resolver = new(
            new ResolvedConversationGeneration(
                generationSnapshot,
                "Follow the confirmed profile."));
        InMemoryGenerationSnapshotStore snapshotStore = new();
        DeterministicStreamingConversationResponder responder = new("Done");
        StreamConversationTurnUseCase useCase = new(
            repository,
            responder,
            new FixedTimeProvider(createdAt.AddMinutes(2)),
            resolver,
            snapshotStore);

        await ConsumeAsync(useCase.ExecuteAsync(
            conversation.Id,
            userMessage.Id,
            TestContext.Current.CancellationToken)).ConfigureAwait(true);

        Assert.Equal(conversation.Id, resolver.LastConversationId);
        Assert.Equal(userMessage.Id, resolver.LastTriggeringUserMessageId);
        Assert.Equal(userMessage.RevisionId, resolver.LastTriggeringUserMessageRevisionId);
        Assert.Equal(new[] { userMessage.RevisionId }, resolver.LastInputMessageRevisionIds);
        ConversationResponseRequest request = Assert.IsType<ConversationResponseRequest>(
            responder.LastRequest);
        Assert.Same(generationSnapshot, request.GenerationSnapshot);
        Assert.Equal("Follow the confirmed profile.", request.SystemInstructions);
        GenerationSnapshot persistedSnapshot = Assert.IsType<GenerationSnapshot>(
            await snapshotStore.FindAsync(
                generationSnapshot.Id,
                TestContext.Current.CancellationToken).ConfigureAwait(true));
        Assert.Same(generationSnapshot, persistedSnapshot);
        Conversation persistedConversation = Assert.IsType<Conversation>(
            await repository.FindAsync(
                conversation.Id,
                TestContext.Current.CancellationToken).ConfigureAwait(true));
        ChatMessage assistant = persistedConversation.Messages[^1];
        Assert.Equal(MessageRole.Assistant, assistant.Role);
        Assert.Equal(generationSnapshot.Id, assistant.GenerationSnapshotId);
    }

    [Fact]
    public async Task CompleteTurnCarriesAndPersistsExactGenerationSnapshot()
    {
        DateTimeOffset createdAt = new(2026, 9, 16, 12, 30, 0, TimeSpan.Zero);
        Conversation conversation = new(
            ConversationId.New(),
            "Bound completion",
            createdAt,
            createdAt);
        ChatMessage userMessage = new(
            MessageId.New(),
            MessageRole.User,
            "Complete with profile",
            createdAt.AddMinutes(1));
        conversation.AddMessage(userMessage);
        InMemoryConversationRepository repository = new();
        await repository.SaveAsync(
            conversation,
            TestContext.Current.CancellationToken).ConfigureAwait(true);
        GenerationSnapshot generationSnapshot = CreateSnapshot(
            conversation.Id,
            userMessage,
            createdAt.AddMinutes(1));
        CapturingGenerationResolver resolver = new(
            new ResolvedConversationGeneration(
                generationSnapshot,
                "Use exact profile instructions."));
        InMemoryGenerationSnapshotStore snapshotStore = new();
        DeterministicConversationResponder responder = new("Completed");
        CompleteConversationTurnUseCase useCase = new(
            repository,
            responder,
            new FixedTimeProvider(createdAt.AddMinutes(2)),
            resolver,
            snapshotStore);

        ChatMessage assistant = await useCase.ExecuteAsync(
            conversation.Id,
            userMessage.Id,
            TestContext.Current.CancellationToken).ConfigureAwait(true);

        ConversationResponseRequest request = Assert.IsType<ConversationResponseRequest>(
            responder.LastRequest);
        Assert.Same(generationSnapshot, request.GenerationSnapshot);
        Assert.Equal("Use exact profile instructions.", request.SystemInstructions);
        Assert.Equal(generationSnapshot.Id, assistant.GenerationSnapshotId);
        Assert.NotNull(await snapshotStore.FindAsync(
            generationSnapshot.Id,
            TestContext.Current.CancellationToken).ConfigureAwait(true));
    }

    [Fact]
    public async Task CompleteTurnFailureDoesNotPersistGenerationSnapshot()
    {
        DateTimeOffset createdAt = new(2026, 9, 17, 9, 30, 0, TimeSpan.Zero);
        Conversation conversation = CreateConversationWithUserMessage(createdAt);
        ChatMessage userMessage = conversation.Messages[^1];
        InMemoryConversationRepository repository = new();
        await repository.SaveAsync(
            conversation,
            TestContext.Current.CancellationToken).ConfigureAwait(true);
        GenerationSnapshot snapshot = CreateSnapshot(
            conversation.Id,
            userMessage,
            createdAt.AddMinutes(1));
        CapturingGenerationResolver resolver = new(
            new ResolvedConversationGeneration(snapshot, systemInstructions: null));
        InMemoryGenerationSnapshotStore snapshotStore = new();
        InvalidOperationException providerFailure = new("provider failed");
        CompleteConversationTurnUseCase useCase = new(
            repository,
            new FailingConversationResponder(providerFailure),
            new FixedTimeProvider(createdAt.AddMinutes(2)),
            resolver,
            snapshotStore);

        InvalidOperationException actual = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            useCase.ExecuteAsync(
                conversation.Id,
                userMessage.Id,
                TestContext.Current.CancellationToken)).ConfigureAwait(true);

        Assert.Same(providerFailure, actual);
        Assert.Equal(0, snapshotStore.SaveCount);
        Assert.Empty(snapshotStore.Snapshots);
        Conversation persisted = Assert.IsType<Conversation>(await repository.FindAsync(
            conversation.Id,
            TestContext.Current.CancellationToken).ConfigureAwait(true));
        Assert.Single(persisted.Messages);
    }

    [Fact]
    public async Task StreamingCancellationDoesNotPersistGenerationSnapshot()
    {
        DateTimeOffset createdAt = new(2026, 9, 17, 9, 45, 0, TimeSpan.Zero);
        Conversation conversation = CreateConversationWithUserMessage(createdAt);
        ChatMessage userMessage = conversation.Messages[^1];
        InMemoryConversationRepository repository = new();
        await repository.SaveAsync(
            conversation,
            TestContext.Current.CancellationToken).ConfigureAwait(true);
        GenerationSnapshot snapshot = CreateSnapshot(
            conversation.Id,
            userMessage,
            createdAt.AddMinutes(1));
        CapturingGenerationResolver resolver = new(
            new ResolvedConversationGeneration(snapshot, systemInstructions: null));
        InMemoryGenerationSnapshotStore snapshotStore = new();
        PausingStreamingConversationResponder responder = new("partial", "never persisted");
        StreamConversationTurnUseCase useCase = new(
            repository,
            responder,
            new FixedTimeProvider(createdAt.AddMinutes(2)),
            resolver,
            snapshotStore);
        using CancellationTokenSource cancellation = new();
        await using IAsyncEnumerator<ConversationResponseChunk> enumerator = useCase
            .ExecuteAsync(conversation.Id, userMessage.Id, cancellation.Token)
            .GetAsyncEnumerator(cancellation.Token);

        Assert.True(await enumerator.MoveNextAsync().ConfigureAwait(true));
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            _ = await enumerator.MoveNextAsync().AsTask().ConfigureAwait(true);
        }).ConfigureAwait(true);

        Assert.Equal(0, snapshotStore.SaveCount);
        Assert.Empty(snapshotStore.Snapshots);
        Conversation persisted = Assert.IsType<Conversation>(await repository.FindAsync(
            conversation.Id,
            TestContext.Current.CancellationToken).ConfigureAwait(true));
        Assert.Single(persisted.Messages);
    }

    [Fact]
    public async Task ConversationSaveFailureRollsBackGenerationSnapshot()
    {
        DateTimeOffset createdAt = new(2026, 9, 17, 10, 0, 0, TimeSpan.Zero);
        Conversation conversation = CreateConversationWithUserMessage(createdAt);
        ChatMessage userMessage = conversation.Messages[^1];
        IOException persistenceFailure = new("conversation save failed");
        FailingSaveConversationRepository repository = new(
            conversation,
            persistenceFailure);
        GenerationSnapshot snapshot = CreateSnapshot(
            conversation.Id,
            userMessage,
            createdAt.AddMinutes(1));
        CapturingGenerationResolver resolver = new(
            new ResolvedConversationGeneration(snapshot, systemInstructions: null));
        InMemoryGenerationSnapshotStore snapshotStore = new();
        CompleteConversationTurnUseCase useCase = new(
            repository,
            new DeterministicConversationResponder("generated"),
            new FixedTimeProvider(createdAt.AddMinutes(2)),
            resolver,
            snapshotStore);

        IOException actual = await Assert.ThrowsAsync<IOException>(() =>
            useCase.ExecuteAsync(
                conversation.Id,
                userMessage.Id,
                TestContext.Current.CancellationToken)).ConfigureAwait(true);

        Assert.Same(persistenceFailure, actual);
        Assert.Equal(1, snapshotStore.SaveCount);
        Assert.Equal(1, snapshotStore.DeleteCount);
        Assert.Empty(snapshotStore.Snapshots);
        Assert.Single(conversation.Messages);
    }

    [Fact]
    public async Task SnapshotBoundGenerationRequiresDurableSnapshotStoreBeforeProviderCall()
    {
        DateTimeOffset createdAt = new(2026, 9, 17, 10, 15, 0, TimeSpan.Zero);
        Conversation conversation = CreateConversationWithUserMessage(createdAt);
        ChatMessage userMessage = conversation.Messages[^1];
        InMemoryConversationRepository repository = new();
        await repository.SaveAsync(
            conversation,
            TestContext.Current.CancellationToken).ConfigureAwait(true);
        GenerationSnapshot snapshot = CreateSnapshot(
            conversation.Id,
            userMessage,
            createdAt.AddMinutes(1));
        CapturingGenerationResolver resolver = new(
            new ResolvedConversationGeneration(snapshot, systemInstructions: null));
        DeterministicConversationResponder responder = new("must not run");
        CompleteConversationTurnUseCase useCase = new(
            repository,
            responder,
            new FixedTimeProvider(createdAt.AddMinutes(2)),
            resolver);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            useCase.ExecuteAsync(
                conversation.Id,
                userMessage.Id,
                TestContext.Current.CancellationToken)).ConfigureAwait(true);

        Assert.Equal(0, responder.CallCount);
    }

    [Fact]
    public void ResponseRequestRejectsSnapshotForDifferentConversation()
    {
        ConversationId requestConversationId = ConversationId.New();
        ConversationId otherConversationId = ConversationId.New();
        ChatMessage message = new(
            MessageId.New(),
            MessageRole.User,
            "Hello",
            DateTimeOffset.UtcNow);
        GenerationSnapshot snapshot = CreateSnapshot(
            otherConversationId,
            message,
            DateTimeOffset.UtcNow);

        Assert.Throws<ArgumentException>(() => new ConversationResponseRequest(
            requestConversationId,
            "Mismatch",
            new[] { message },
            snapshot));
    }

    [Fact]
    public void ResponseRequestRejectsSnapshotWhoseInputRevisionDoesNotMatchMessages()
    {
        ConversationId conversationId = ConversationId.New();
        ChatMessage message = new(
            MessageId.New(),
            MessageRole.User,
            "Hello",
            DateTimeOffset.UtcNow);
        MessageRevisionId differentRevisionId = MessageRevisionId.New();
        GenerationSnapshot snapshot = new(
            GenerationSnapshotId.New(),
            conversationId,
            message.Id,
            differentRevisionId,
            new[] { differentRevisionId },
            DateTimeOffset.UtcNow,
            new InferenceProviderConfiguration(
                "provider.alpha",
                "owner/model"));

        Assert.Throws<ArgumentException>(() => new ConversationResponseRequest(
            conversationId,
            "Mismatch",
            new[] { message },
            snapshot));
    }

    private static Conversation CreateConversationWithUserMessage(DateTimeOffset createdAt)
    {
        Conversation conversation = new(
            ConversationId.New(),
            "Snapshot failure contract",
            createdAt,
            createdAt);
        conversation.AddMessage(new ChatMessage(
            MessageId.New(),
            MessageRole.User,
            "Generate",
            createdAt.AddMinutes(1)));
        return conversation;
    }

    private static GenerationSnapshot CreateSnapshot(
        ConversationId conversationId,
        ChatMessage triggeringMessage,
        DateTimeOffset capturedAt)
    {
        return new GenerationSnapshot(
            GenerationSnapshotId.New(),
            conversationId,
            triggeringMessage.Id,
            triggeringMessage.RevisionId,
            new[] { triggeringMessage.RevisionId },
            capturedAt,
            new InferenceProviderConfiguration(
                "provider.alpha",
                "owner/model"),
            GenerationProfileId.New(),
            GenerationProfileRevisionId.New());
    }

    private static async Task ConsumeAsync(
        IAsyncEnumerable<ConversationResponseChunk> chunks)
    {
        await foreach (ConversationResponseChunk _ in chunks.ConfigureAwait(true))
        {
        }
    }

    private sealed class FailingSaveConversationRepository : IConversationRepository
    {
        private readonly Conversation _conversation;
        private readonly Exception _saveException;

        public FailingSaveConversationRepository(
            Conversation conversation,
            Exception saveException)
        {
            _conversation = conversation;
            _saveException = saveException;
        }

        public Task<Conversation?> FindAsync(
            ConversationId conversationId,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<Conversation?>(
                conversationId == _conversation.Id ? _conversation : null);
        }

        public Task<IReadOnlyList<ConversationSummary>> ListAsync(
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<IReadOnlyList<ConversationSummary>>(
                [ConversationSummary.FromConversation(_conversation)]);
        }

        public Task SaveAsync(
            Conversation conversation,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(conversation);
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromException(_saveException);
        }

        public Task<bool> DeleteAsync(
            ConversationId conversationId,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(false);
        }
    }

    private sealed class CapturingGenerationResolver : IConversationGenerationResolver
    {
        private readonly ResolvedConversationGeneration _resolved;

        public CapturingGenerationResolver(ResolvedConversationGeneration resolved)
        {
            _resolved = resolved;
        }

        public ConversationId? LastConversationId { get; private set; }

        public MessageId? LastTriggeringUserMessageId { get; private set; }

        public MessageRevisionId? LastTriggeringUserMessageRevisionId { get; private set; }

        public IReadOnlyList<MessageRevisionId>? LastInputMessageRevisionIds { get; private set; }

        public Task<ResolvedConversationGeneration?> ResolveAsync(
            ConversationId conversationId,
            MessageId triggeringUserMessageId,
            MessageRevisionId triggeringUserMessageRevisionId,
            IReadOnlyList<MessageRevisionId> inputMessageRevisionIds,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LastConversationId = conversationId;
            LastTriggeringUserMessageId = triggeringUserMessageId;
            LastTriggeringUserMessageRevisionId = triggeringUserMessageRevisionId;
            LastInputMessageRevisionIds = inputMessageRevisionIds.ToArray();
            return Task.FromResult<ResolvedConversationGeneration?>(_resolved);
        }
    }
}
