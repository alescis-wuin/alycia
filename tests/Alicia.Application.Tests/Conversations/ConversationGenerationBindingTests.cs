using Alicia.Application.Conversations;
using Alicia.Application.Generations;
using Alicia.Application.Providers;
using Alicia.Domain.Conversations;

namespace Alicia.Application.Tests.Conversations;

public sealed class ConversationGenerationBindingTests
{
    [Fact]
    public async Task StreamingTurnCarriesResolvedSnapshotAndSystemInstructions()
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
            userMessage.Id,
            createdAt.AddMinutes(1));
        CapturingGenerationResolver resolver = new(
            new ResolvedConversationGeneration(
                generationSnapshot,
                "Follow the confirmed profile."));
        DeterministicStreamingConversationResponder responder = new("Done");
        StreamConversationTurnUseCase useCase = new(
            repository,
            responder,
            new FixedTimeProvider(createdAt.AddMinutes(2)),
            resolver);

        await ConsumeAsync(useCase.ExecuteAsync(
            conversation.Id,
            userMessage.Id,
            TestContext.Current.CancellationToken)).ConfigureAwait(true);

        Assert.Equal(conversation.Id, resolver.LastConversationId);
        Assert.Equal(userMessage.Id, resolver.LastTriggeringUserMessageId);
        ConversationResponseRequest request = Assert.IsType<ConversationResponseRequest>(
            responder.LastRequest);
        Assert.Same(generationSnapshot, request.GenerationSnapshot);
        Assert.Equal("Follow the confirmed profile.", request.SystemInstructions);
    }

    [Fact]
    public async Task CompleteTurnCarriesResolvedSnapshotAndSystemInstructions()
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
            userMessage.Id,
            createdAt.AddMinutes(1));
        CapturingGenerationResolver resolver = new(
            new ResolvedConversationGeneration(
                generationSnapshot,
                "Use exact profile instructions."));
        DeterministicConversationResponder responder = new("Completed");
        CompleteConversationTurnUseCase useCase = new(
            repository,
            responder,
            new FixedTimeProvider(createdAt.AddMinutes(2)),
            resolver);

        await useCase.ExecuteAsync(
            conversation.Id,
            userMessage.Id,
            TestContext.Current.CancellationToken).ConfigureAwait(true);

        ConversationResponseRequest request = Assert.IsType<ConversationResponseRequest>(
            responder.LastRequest);
        Assert.Same(generationSnapshot, request.GenerationSnapshot);
        Assert.Equal("Use exact profile instructions.", request.SystemInstructions);
    }

    [Fact]
    public void ResponseRequestRejectsSnapshotForDifferentConversation()
    {
        ConversationId requestConversationId = ConversationId.New();
        ConversationId otherConversationId = ConversationId.New();
        GenerationSnapshot snapshot = CreateSnapshot(
            otherConversationId,
            MessageId.New(),
            DateTimeOffset.UtcNow);
        List<ChatMessage> messages = new()
        {
            new ChatMessage(
                MessageId.New(),
                MessageRole.User,
                "Hello",
                DateTimeOffset.UtcNow),
        };

        Assert.Throws<ArgumentException>(() => new ConversationResponseRequest(
            requestConversationId,
            "Mismatch",
            messages,
            snapshot));
    }

    private static GenerationSnapshot CreateSnapshot(
        ConversationId conversationId,
        MessageId messageId,
        DateTimeOffset capturedAt)
    {
        return new GenerationSnapshot(
            conversationId,
            messageId,
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

    private sealed class CapturingGenerationResolver : IConversationGenerationResolver
    {
        private readonly ResolvedConversationGeneration _resolved;

        public CapturingGenerationResolver(ResolvedConversationGeneration resolved)
        {
            _resolved = resolved;
        }

        public ConversationId? LastConversationId { get; private set; }

        public MessageId? LastTriggeringUserMessageId { get; private set; }

        public Task<ResolvedConversationGeneration?> ResolveAsync(
            ConversationId conversationId,
            MessageId triggeringUserMessageId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LastConversationId = conversationId;
            LastTriggeringUserMessageId = triggeringUserMessageId;
            return Task.FromResult<ResolvedConversationGeneration?>(_resolved);
        }
    }
}
