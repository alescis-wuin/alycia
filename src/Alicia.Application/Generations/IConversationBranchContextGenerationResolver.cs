using Alicia.Domain.Conversations;

namespace Alicia.Application.Generations;

public interface IConversationBranchContextGenerationResolver : IConversationContextGenerationResolver
{
    Task<ResolvedConversationGeneration?> ResolveAsync(
        ConversationId conversationId,
        ConversationBranchId branchId,
        MessageId triggeringUserMessageId,
        MessageRevisionId triggeringUserMessageRevisionId,
        IReadOnlyList<MessageRevisionId> inputMessageRevisionIds,
        ConversationContextRevision? contextRevision,
        CancellationToken cancellationToken = default);
}
