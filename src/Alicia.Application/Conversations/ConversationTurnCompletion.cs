using Alicia.Application.Generations;
using Alicia.Domain.Conversations;

namespace Alicia.Application.Conversations;

internal static class ConversationTurnCompletion
{
    public static void RequireSnapshotStore(
        ResolvedConversationGeneration? resolvedGeneration,
        IGenerationSnapshotStore? snapshotStore)
    {
        if (resolvedGeneration is not null && snapshotStore is null)
        {
            throw new InvalidOperationException(
                "A generation-snapshot store is required for snapshot-bound conversation generation.");
        }
    }

    public static async Task<ChatMessage> PersistAsync(
        IConversationRepository repository,
        IGenerationSnapshotStore? snapshotStore,
        Conversation currentConversation,
        string responseContent,
        DateTimeOffset assistantCreatedAt,
        ResolvedConversationGeneration? resolvedGeneration,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(currentConversation);

        GenerationSnapshot? snapshot = resolvedGeneration?.Snapshot;
        GenerationSnapshotId? snapshotId = snapshot?.Id;
        ChatMessage assistantMessage = new(
            MessageId.New(),
            MessageRevisionId.New(),
            parentRevisionId: null,
            MessageRole.Assistant,
            responseContent,
            assistantCreatedAt,
            snapshotId);
        Conversation updatedConversation = CloneAndAppend(
            currentConversation,
            assistantMessage);

        if (snapshot is null)
        {
            await repository
                .SaveAsync(updatedConversation, cancellationToken)
                .ConfigureAwait(false);
            currentConversation.AddMessage(assistantMessage);
            return assistantMessage;
        }

        IGenerationSnapshotStore requiredStore = snapshotStore
            ?? throw new InvalidOperationException(
                "A generation-snapshot store is required for snapshot-bound conversation generation.");

        await requiredStore
            .SaveAsync(snapshot, cancellationToken)
            .ConfigureAwait(false);

        bool conversationSaved = false;
        try
        {
            await repository
                .SaveAsync(updatedConversation, cancellationToken)
                .ConfigureAwait(false);
            conversationSaved = true;
        }
        finally
        {
            if (!conversationSaved)
            {
                await requiredStore
                    .DeleteAsync(snapshot.Id, CancellationToken.None)
                    .ConfigureAwait(false);
            }
        }

        currentConversation.AddMessage(assistantMessage);
        return assistantMessage;
    }

    private static Conversation CloneAndAppend(
        Conversation source,
        ChatMessage assistantMessage)
    {
        Conversation copy = Conversation.Restore(
            source.Id,
            source.Title,
            source.CreatedAt,
            source.UpdatedAt,
            source.MessageRevisions,
            source.ContextRevisions,
            source.Branches,
            source.ActiveBranchId);
        copy.AddMessage(assistantMessage);
        return copy;
    }
}
