using Alicia.Domain.Conversations;

namespace Alicia.Application.Conversations;

public sealed class UpdateConversationContextUseCase
{
    private readonly IConversationRepository _repository;
    private readonly TimeProvider _timeProvider;

    public UpdateConversationContextUseCase(
        IConversationRepository repository,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(timeProvider);
        _repository = repository;
        _timeProvider = timeProvider;
    }

    public async Task<ConversationContextRevision> ExecuteAsync(
        ConversationId conversationId,
        string? instructions,
        bool replaceProfileInstructions,
        CancellationToken cancellationToken = default)
    {
        if (conversationId.IsEmpty)
        {
            throw new ArgumentException(
                "Conversation identifier cannot be empty.",
                nameof(conversationId));
        }

        Conversation? conversation = await _repository
            .FindAsync(conversationId, cancellationToken)
            .ConfigureAwait(false);

        if (conversation is null)
        {
            throw new KeyNotFoundException(
                $"Conversation '{conversationId}' was not found.");
        }

        ConversationContextRevision revision = conversation.UpdateContext(
            instructions,
            replaceProfileInstructions,
            _timeProvider.GetUtcNow());
        await _repository
            .SaveAsync(conversation, cancellationToken)
            .ConfigureAwait(false);
        return revision;
    }
}
