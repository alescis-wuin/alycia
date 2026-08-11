namespace Alicia.Application.Conversations;

public interface IStreamingConversationResponder
{
    IAsyncEnumerable<ConversationResponseChunk> StreamAsync(
        ConversationResponseRequest request,
        CancellationToken cancellationToken = default);
}
