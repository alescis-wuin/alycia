using System.Runtime.CompilerServices;
using System.Text;
using Alicia.Application.Generations;
using Alicia.Domain.Conversations;

namespace Alicia.Application.Conversations;

public sealed class StreamConversationTurnUseCase
{
    private readonly IConversationRepository _repository;
    private readonly IStreamingConversationResponder _responder;
    private readonly TimeProvider _timeProvider;
    private readonly IConversationGenerationResolver? _generationResolver;

    public StreamConversationTurnUseCase(
        IConversationRepository repository,
        IStreamingConversationResponder responder,
        TimeProvider timeProvider,
        IConversationGenerationResolver? generationResolver = null)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(responder);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _repository = repository;
        _responder = responder;
        _timeProvider = timeProvider;
        _generationResolver = generationResolver;
    }

    public async IAsyncEnumerable<ConversationResponseChunk> ExecuteAsync(
        ConversationId conversationId,
        MessageId triggeringUserMessageId,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
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

        ConversationTurnSnapshot snapshot = ConversationTurnSnapshot.Capture(
            conversation,
            triggeringUserMessageId);
        ResolvedConversationGeneration? resolvedGeneration = _generationResolver is null
            ? null
            : await _generationResolver
                .ResolveAsync(
                    conversationId,
                    triggeringUserMessageId,
                    cancellationToken)
                .ConfigureAwait(false);
        ConversationResponseRequest request =
            ConversationResponseRequest.FromConversation(
                conversation,
                resolvedGeneration);
        StringBuilder responseContent = new();

        await foreach (ConversationResponseChunk chunk in _responder
            .StreamAsync(request, cancellationToken)
            .WithCancellation(cancellationToken)
            .ConfigureAwait(false))
        {
            if (chunk is null)
            {
                throw new InvalidOperationException(
                    "Streaming conversation responder returned an invalid response chunk.");
            }

            if (chunk.Kind == ConversationResponseChunkKind.Content)
            {
                responseContent.Append(chunk.TextDelta);
            }

            yield return chunk;
        }

        cancellationToken.ThrowIfCancellationRequested();

        string content = responseContent.ToString();

        if (string.IsNullOrWhiteSpace(content))
        {
            throw new InvalidOperationException(
                "Streaming conversation responder returned no response content.");
        }

        ConversationResponse response = new(content);
        Conversation? reloadedConversation = await _repository
            .FindAsync(conversationId, cancellationToken)
            .ConfigureAwait(false);
        Conversation currentConversation = snapshot.RequireMatching(reloadedConversation);

        ChatMessage assistantMessage = new(
            MessageId.New(),
            MessageRole.Assistant,
            response.Content,
            _timeProvider.GetUtcNow());

        currentConversation.AddMessage(assistantMessage);

        await _repository
            .SaveAsync(currentConversation, cancellationToken)
            .ConfigureAwait(false);
    }
}
