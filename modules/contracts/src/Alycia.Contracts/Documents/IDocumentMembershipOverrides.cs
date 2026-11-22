namespace Alycia.Contracts.Documents;

// The caller changes membership and drops its old overrides in the same transaction.
public interface IDocumentMembershipOverrides
{
    Task ClearAsync(Guid conversation, CancellationToken ct);
}
