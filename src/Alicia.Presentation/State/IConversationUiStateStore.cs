namespace Alicia.Presentation.State;

public interface IConversationUiStateStore
{
    Task<ConversationUiStateSnapshot> LoadAsync(
        CancellationToken cancellationToken = default);

    Task SaveAsync(
        ConversationUiStateSnapshot snapshot,
        CancellationToken cancellationToken = default);
}
