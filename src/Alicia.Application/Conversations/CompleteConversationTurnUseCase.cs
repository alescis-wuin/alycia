using Alicia.Application.Generations;
using Alicia.Domain.Conversations;

namespace Alicia.Application.Conversations;

public sealed class CompleteConversationTurnUseCase
{
    private readonly IConversationRepository _repository;
    private readonly IConversationResponder _responder;
    private readonly TimeProvider _timeProvider;
    private readonly IConversationGenerationResolver? _generationResolver;
    private readonly IGenerationSnapshotStore? _generationSnapshotStore;

    public CompleteConversationTurnUseCase(
        IConversationRepository repository,
        IConversationResponder responder,
        TimeProvider timeProvider,
        IConversationGenerationResolver? generationResolver = null,
        IGenerationSnapshotStore? generationSnapshotStore = null)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(responder);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _repository = repository;
        _responder = responder;
        _timeProvider = timeProvider;
        _generationResolver = generationResolver;
        _generationSnapshotStore = generationSnapshotStore;
    }

    public async Task<ChatMessage> ExecuteAsync(
        ConversationId conversationId,
        MessageId triggeringUserMessageId,
        CancellationToken cancellationToken = default)
    {
        if (conversationId.IsEmpty)
        {
            throw new ArgumentException(
                "Conversation identifier cannot be empty.",
                nameof(conversationId));
        }

        if (triggeringUserMessageId.IsEmpty)
        {
            throw new ArgumentException(
                "Triggering message identifier cannot be empty.",
                nameof(triggeringUserMessageId));
        }

        Conversation? conversation = await _repository
            .FindAsync(conversationId, cancellationToken)
            .ConfigureAwait(false);

        if (conversation is null)
        {
            throw new KeyNotFoundException(
                $"Conversation '{conversationId}' was not found.");
        }

        ConversationTurnSnapshot turnSnapshot = ConversationTurnSnapshot.Capture(
            conversation,
            triggeringUserMessageId);
        ResolvedConversationGeneration? resolvedGeneration =
            await ConversationGenerationResolution.ResolveAsync(
                    _generationResolver,
                    conversationId,
                    triggeringUserMessageId,
                    turnSnapshot,
                    cancellationToken)
                .ConfigureAwait(false);
        ConversationTurnCompletion.RequireSnapshotStore(
            resolvedGeneration,
            _generationSnapshotStore);
        ConversationResponseRequest request =
            ConversationResponseRequest.FromConversation(
                conversation,
                resolvedGeneration);

        ConversationResponse? response = await _responder
            .GenerateAsync(request, cancellationToken)
            .ConfigureAwait(false);

        if (response is null)
        {
            throw new InvalidOperationException(
                "Conversation responder returned no response.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        Conversation? reloadedConversation = await _repository
            .FindAsync(conversationId, cancellationToken)
            .ConfigureAwait(false);
        Conversation currentConversation = turnSnapshot.RequireMatching(reloadedConversation);

        return await ConversationTurnCompletion
            .PersistAsync(
                _repository,
                _generationSnapshotStore,
                currentConversation,
                response.Content,
                _timeProvider.GetUtcNow(),
                resolvedGeneration,
                cancellationToken)
            .ConfigureAwait(false);
    }
}
