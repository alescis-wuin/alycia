namespace Alycia.Contracts.Documents;

public interface IConversationWorkspaceLookup
{
    Task<Guid?> FindWorkspaceAsync(Guid conversation, CancellationToken ct);
}
