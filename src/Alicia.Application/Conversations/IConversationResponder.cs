namespace Alicia.Application.Conversations;

public interface IConversationResponder
{
    Task<ConversationResponse> GenerateAsync(
        ConversationResponseRequest request,
        CancellationToken cancellationToken = default);
}
