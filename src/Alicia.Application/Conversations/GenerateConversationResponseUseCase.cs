using Alicia.Domain.Conversations;

namespace Alicia.Application.Conversations;

public sealed class GenerateConversationResponseUseCase
{
    private readonly IConversationRepository _repository;
    private readonly IConversationResponder _responder;

    public GenerateConversationResponseUseCase(
        IConversationRepository repository,
        IConversationResponder responder)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(responder);

        _repository = repository;
        _responder = responder;
    }

    public async Task<ConversationResponse> ExecuteAsync(
        ConversationId conversationId,
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

        ConversationResponseRequest request =
            ConversationResponseRequest.FromConversation(conversation);

        ConversationResponse? response = await _responder
            .GenerateAsync(request, cancellationToken)
            .ConfigureAwait(false);

        return response
            ?? throw new InvalidOperationException(
                "Conversation responder returned no response.");
    }
}
