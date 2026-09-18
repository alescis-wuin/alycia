using Alicia.Application.Generations;
using Alicia.Domain.Conversations;

namespace Alicia.Application.Conversations;

public sealed class EditMessageUseCase
{
    private readonly IConversationRepository _repository;
    private readonly TimeProvider _timeProvider;
    private readonly IConversationBranchGenerationSelectionStore? _generationSelectionStore;

    public EditMessageUseCase(
        IConversationRepository repository,
        TimeProvider timeProvider,
        IConversationBranchGenerationSelectionStore? generationSelectionStore = null)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _repository = repository;
        _timeProvider = timeProvider;
        _generationSelectionStore = generationSelectionStore;
    }

    public async Task<ChatMessage> ExecuteAsync(
        ConversationId conversationId,
        MessageRevisionId messageRevisionId,
        string content,
        CancellationToken cancellationToken = default)
    {
        if (conversationId.IsEmpty)
        {
            throw new ArgumentException(
                "Conversation identifier cannot be empty.",
                nameof(conversationId));
        }

        if (messageRevisionId.IsEmpty)
        {
            throw new ArgumentException(
                "Message revision identifier cannot be empty.",
                nameof(messageRevisionId));
        }

        Conversation? currentConversation = await _repository
            .FindAsync(conversationId, cancellationToken)
            .ConfigureAwait(false);

        if (currentConversation is null)
        {
            throw new KeyNotFoundException(
                $"Conversation '{conversationId}' was not found.");
        }

        ConversationBranchId parentBranchId = currentConversation.ActiveBranchId;
        ConversationGenerationSelection? inheritedSelection = _generationSelectionStore is null
            ? null
            : await _generationSelectionStore
                .LoadAsync(conversationId, parentBranchId, cancellationToken)
                .ConfigureAwait(false);
        Conversation updatedConversation = Clone(currentConversation);
        ChatMessage editedRevision = updatedConversation.EditUserMessage(
            messageRevisionId,
            content,
            _timeProvider.GetUtcNow());
        ConversationBranchId childBranchId = updatedConversation.ActiveBranchId;
        bool childSelectionSaved = false;

        if (_generationSelectionStore is not null && inheritedSelection is not null)
        {
            await _generationSelectionStore
                .SaveAsync(
                    inheritedSelection.ForBranch(childBranchId),
                    cancellationToken)
                .ConfigureAwait(false);
            childSelectionSaved = true;
        }

        bool conversationSaved = false;
        try
        {
            await _repository
                .SaveAsync(updatedConversation, cancellationToken)
                .ConfigureAwait(false);
            conversationSaved = true;
        }
        finally
        {
            if (!conversationSaved
                && childSelectionSaved
                && _generationSelectionStore is not null)
            {
                await _generationSelectionStore
                    .DeleteAsync(
                        conversationId,
                        childBranchId,
                        CancellationToken.None)
                    .ConfigureAwait(false);
            }
        }

        return editedRevision;
    }

    private static Conversation Clone(Conversation source)
    {
        return Conversation.Restore(
            source.Id,
            source.Title,
            source.CreatedAt,
            source.UpdatedAt,
            source.MessageRevisions,
            source.ContextRevisions,
            source.Branches,
            source.ActiveBranchId);
    }
}
