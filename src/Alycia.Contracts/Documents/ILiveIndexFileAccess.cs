namespace Alycia.Contracts.Documents;

// Evaluate current membership, project status and effective file mode in one fresh database snapshot.
public interface ILiveIndexFileAccess
{
    Task<bool> IsAllowedAsync(Guid owner, bool project, Guid file, CancellationToken ct);
}
