using Alicia.Application.Conversations;
using Alicia.Domain.Conversations;

namespace Alicia.Infrastructure.Conversations;

public sealed class DevelopmentConversationResponder : IConversationResponder
{
    private readonly TimeSpan _responseDelay;

    public DevelopmentConversationResponder(TimeSpan responseDelay)
    {
        if (responseDelay < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(responseDelay),
                responseDelay,
                "Response delay cannot be negative.");
        }

        _responseDelay = responseDelay;
    }

    public async Task<ConversationResponse> GenerateAsync(
        ConversationResponseRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        ChatMessage? lastUserMessage = request.Messages
            .LastOrDefault(message => message.Role == MessageRole.User);

        if (lastUserMessage is null)
        {
            throw new InvalidOperationException(
                "A development response requires at least one user message.");
        }

        await Task.Delay(_responseDelay, cancellationToken).ConfigureAwait(false);

        return new ConversationResponse(
            $"Local development response to: {lastUserMessage.Content}");
    }
}
