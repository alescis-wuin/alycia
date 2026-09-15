using Alicia.Domain.Conversations;

namespace Alicia.Application.Conversations;

public sealed class CreateConversationUseCase
{
    private readonly IConversationRepository _repository;
    private readonly TimeProvider _timeProvider;

    public CreateConversationUseCase(
        IConversationRepository repository,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _repository = repository;
        _timeProvider = timeProvider;
    }

    public async Task<Conversation> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        Conversation conversation = new(
            ConversationId.New(),
            _timeProvider.GetUtcNow());

        await _repository
            .SaveAsync(conversation, cancellationToken)
            .ConfigureAwait(false);

        return conversation;
    }
}
