namespace Alicia.Presentation.State;

public sealed class TransientConversationUiStateStore : IConversationUiStateStore
{
    private ConversationUiStateSnapshot _snapshot = ConversationUiStateSnapshot.Default;

    public Task<ConversationUiStateSnapshot> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_snapshot);
    }

    public Task SaveAsync(
        ConversationUiStateSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        cancellationToken.ThrowIfCancellationRequested();
        _snapshot = snapshot;
        return Task.CompletedTask;
    }
}
