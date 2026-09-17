using Alicia.Domain.Conversations;

namespace Alicia.Application.Generations;

public interface IConversationGenerationResolver
{
    Task<ResolvedConversationGeneration?> ResolveAsync(
        ConversationId conversationId,
        MessageId triggeringUserMessageId,
        MessageRevisionId triggeringUserMessageRevisionId,
        IReadOnlyList<MessageRevisionId> inputMessageRevisionIds,
        CancellationToken cancellationToken = default);
}
