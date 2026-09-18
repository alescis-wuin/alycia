using Alicia.Domain.Conversations;

namespace Alicia.Application.Generations;

public interface IConversationContextGenerationResolver : IConversationGenerationResolver
{
    Task<ResolvedConversationGeneration?> ResolveAsync(
        ConversationId conversationId,
        MessageId triggeringUserMessageId,
        MessageRevisionId triggeringUserMessageRevisionId,
        IReadOnlyList<MessageRevisionId> inputMessageRevisionIds,
        ConversationContextRevision? contextRevision,
        CancellationToken cancellationToken = default);
}
