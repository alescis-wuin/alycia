using Alicia.Application.Generations;
using Alicia.Domain.Conversations;

namespace Alicia.Application.Conversations;

internal static class ConversationGenerationResolution
{
    public static async Task<ResolvedConversationGeneration?> ResolveAsync(
        IConversationGenerationResolver? resolver,
        ConversationId conversationId,
        MessageId triggeringUserMessageId,
        ConversationTurnSnapshot turnSnapshot,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(turnSnapshot);

        if (resolver is null)
        {
            if (turnSnapshot.ActiveContextRevision is not null)
            {
                throw new InvalidOperationException(
                    "Revisioned conversation context requires a context-aware generation resolver.");
            }

            return null;
        }

        ResolvedConversationGeneration? resolved;
        if (resolver is IConversationBranchContextGenerationResolver branchContextResolver)
        {
            resolved = await branchContextResolver.ResolveAsync(
                    conversationId,
                    turnSnapshot.ActiveBranchId,
                    triggeringUserMessageId,
                    turnSnapshot.TriggeringUserMessageRevisionId,
                    turnSnapshot.InputMessageRevisionIds,
                    turnSnapshot.ActiveContextRevision,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        else if (resolver is IConversationContextGenerationResolver contextResolver)
        {
            resolved = await contextResolver.ResolveAsync(
                    conversationId,
                    triggeringUserMessageId,
                    turnSnapshot.TriggeringUserMessageRevisionId,
                    turnSnapshot.InputMessageRevisionIds,
                    turnSnapshot.ActiveContextRevision,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        else
        {
            if (turnSnapshot.ActiveContextRevision is not null)
            {
                throw new InvalidOperationException(
                    "Revisioned conversation context cannot be ignored by the configured generation resolver.");
            }

            resolved = await resolver.ResolveAsync(
                    conversationId,
                    triggeringUserMessageId,
                    turnSnapshot.TriggeringUserMessageRevisionId,
                    turnSnapshot.InputMessageRevisionIds,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        if (turnSnapshot.ActiveContextRevision is not null && resolved is null)
        {
            throw new InvalidOperationException(
                "Revisioned conversation context requires a snapshot-bound generation selection.");
        }

        return resolved;
    }
}
