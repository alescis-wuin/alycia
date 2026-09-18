using Alicia.Application.Conversations;
using Alicia.Application.Generations;
using Alicia.Application.Providers;
using Alicia.Domain.Conversations;

namespace Alicia.Application.Tests.Conversations;

public sealed class ConversationContextUseCaseTests
{
    private static readonly DateTimeOffset _createdAt =
        new(2026, 9, 18, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task UpdateContextCreatesAndPersistsConfirmedRevision()
    {
        InMemoryConversationRepository repository = new();
        Conversation conversation = new(ConversationId.New(), _createdAt);
        await repository.SaveAsync(conversation, CancellationToken.None).ConfigureAwait(true);
        UpdateConversationContextUseCase useCase = new(
            repository,
            new FixedTimeProvider(_createdAt.AddMinutes(1)));

        ConversationContextRevision revision = await useCase.ExecuteAsync(
            conversation.Id,
            "Persistent conversation instructions",
            replaceProfileInstructions: true,
            CancellationToken.None).ConfigureAwait(true);

        Assert.Same(revision, conversation.ActiveContextRevision);
        Assert.True(revision.ReplaceProfileInstructions);
        Assert.Equal("Persistent conversation instructions", revision.Instructions);
        Assert.Equal(2, repository.SaveCount);
    }

    [Fact]
    public async Task UpdateContextRejectsUnknownConversation()
    {
        InMemoryConversationRepository repository = new();
        UpdateConversationContextUseCase useCase = new(
            repository,
            new FixedTimeProvider(_createdAt));

        await Assert.ThrowsAsync<KeyNotFoundException>(() => useCase.ExecuteAsync(
            ConversationId.New(),
            "Missing",
            false,
            CancellationToken.None)).ConfigureAwait(true);

        Assert.Equal(0, repository.SaveCount);
    }

    [Fact]
    public async Task CompleteTurnRejectsLegacyResolverWhenRevisionedContextIsActive()
    {
        InMemoryConversationRepository repository = new();
        Conversation conversation = new(ConversationId.New(), _createdAt);
        conversation.UpdateContext("Context", false, _createdAt.AddMinutes(1));
        ChatMessage user = new(
            MessageId.New(),
            MessageRole.User,
            "Question",
            _createdAt.AddMinutes(2));
        conversation.AddMessage(user);
        await repository.SaveAsync(conversation, CancellationToken.None).ConfigureAwait(true);
        DeterministicConversationResponder responder = new("Answer");
        CompleteConversationTurnUseCase useCase = new(
            repository,
            responder,
            new FixedTimeProvider(_createdAt.AddMinutes(3)),
            new LegacyResolver());

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => useCase.ExecuteAsync(
                conversation.Id,
                user.Id,
                CancellationToken.None)).ConfigureAwait(true);

        Assert.Contains("context", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, responder.CallCount);
    }

    [Fact]
    public async Task CompleteTurnRejectsContextMutationWhileGenerationIsInFlight()
    {
        InMemoryConversationRepository repository = new();
        Conversation conversation = new(ConversationId.New(), _createdAt);
        conversation.UpdateContext("Initial", false, _createdAt.AddMinutes(1));
        ChatMessage user = new(
            MessageId.New(),
            MessageRole.User,
            "Question",
            _createdAt.AddMinutes(2));
        conversation.AddMessage(user);
        await repository.SaveAsync(conversation, CancellationToken.None).ConfigureAwait(true);
        InMemoryGenerationSnapshotStore snapshotStore = new();
        ContextAwareResolver resolver = new(_createdAt.AddMinutes(2).AddSeconds(30));
        DelegateConversationResponder responder = new(async (_, cancellationToken) =>
        {
            conversation.UpdateContext(
                "Changed while responding",
                false,
                _createdAt.AddMinutes(3));
            await repository.SaveAsync(conversation, cancellationToken).ConfigureAwait(false);
            return new ConversationResponse("Stale answer");
        });
        CompleteConversationTurnUseCase useCase = new(
            repository,
            responder,
            new FixedTimeProvider(_createdAt.AddMinutes(4)),
            resolver,
            snapshotStore);

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => useCase.ExecuteAsync(
                conversation.Id,
                user.Id,
                CancellationToken.None)).ConfigureAwait(true);

        Assert.Contains(
            "changed while Alicia was responding",
            exception.Message,
            StringComparison.Ordinal);
        Assert.Equal(0, snapshotStore.SaveCount);
        Assert.Equal(2, conversation.ContextRevisions.Count);
    }

    private sealed class LegacyResolver : IConversationGenerationResolver
    {
        public Task<ResolvedConversationGeneration?> ResolveAsync(
            ConversationId conversationId,
            MessageId triggeringUserMessageId,
            MessageRevisionId triggeringUserMessageRevisionId,
            IReadOnlyList<MessageRevisionId> inputMessageRevisionIds,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<ResolvedConversationGeneration?>(null);
        }
    }

    private sealed class ContextAwareResolver : IConversationContextGenerationResolver
    {
        private readonly DateTimeOffset _capturedAt;

        public ContextAwareResolver(DateTimeOffset capturedAt)
        {
            _capturedAt = capturedAt;
        }

        public Task<ResolvedConversationGeneration?> ResolveAsync(
            ConversationId conversationId,
            MessageId triggeringUserMessageId,
            MessageRevisionId triggeringUserMessageRevisionId,
            IReadOnlyList<MessageRevisionId> inputMessageRevisionIds,
            CancellationToken cancellationToken = default)
        {
            return ResolveAsync(
                conversationId,
                triggeringUserMessageId,
                triggeringUserMessageRevisionId,
                inputMessageRevisionIds,
                contextRevision: null,
                cancellationToken);
        }

        public Task<ResolvedConversationGeneration?> ResolveAsync(
            ConversationId conversationId,
            MessageId triggeringUserMessageId,
            MessageRevisionId triggeringUserMessageRevisionId,
            IReadOnlyList<MessageRevisionId> inputMessageRevisionIds,
            ConversationContextRevision? contextRevision,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            InferenceProviderConfiguration configuration = new(
                "provider.test",
                "model.test",
                contextSize: 4096,
                generation: new InferenceGenerationOptions(maxOutputTokens: 512));
            GenerationContextBudget budget =
                GenerationContextBudget.FromConfiguration(configuration);
            GenerationSnapshot snapshot = new(
                GenerationSnapshotId.New(),
                conversationId,
                triggeringUserMessageId,
                triggeringUserMessageRevisionId,
                inputMessageRevisionIds,
                _capturedAt,
                configuration,
                profileId: null,
                profileRevisionId: null,
                contextRevision?.RevisionId,
                budget);
            ResolvedConversationGeneration resolved = new(
                snapshot,
                contextRevision?.Instructions);
            return Task.FromResult<ResolvedConversationGeneration?>(resolved);
        }
    }
}
