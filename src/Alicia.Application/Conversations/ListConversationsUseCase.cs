namespace Alicia.Application.Conversations;

public sealed class ListConversationsUseCase
{
    private readonly IConversationRepository _repository;

    public ListConversationsUseCase(IConversationRepository repository)
    {
        ArgumentNullException.ThrowIfNull(repository);
        _repository = repository;
    }

    public Task<IReadOnlyList<ConversationSummary>> ExecuteAsync(
        CancellationToken cancellationToken = default)
    {
        return _repository.ListAsync(cancellationToken);
    }
}
