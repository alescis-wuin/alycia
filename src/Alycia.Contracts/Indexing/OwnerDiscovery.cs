namespace Alycia.Contracts.Indexing;

public sealed record ConversationIndexOwner(Guid Id, Guid? WorkspaceId, int Revision, IndexParameters? Override, int MessageCount = 0);
public sealed record IndexHistoryMessage(Guid ConversationId, Guid RequestId, int Sequence, string Role, string Content, DateTimeOffset CreatedAtUtc);
public sealed record ProjectIndexOwner(Guid Id, int Revision);

public interface IConversationIndexOwners
{
    Task<bool> ExistsAsync(Guid id, CancellationToken ct);
    Task<ConversationIndexOwner?> FindAsync(Guid id, CancellationToken ct);
    Task<IndexHistoryMessage[]> HistoryPageAsync(Guid owner, int after, int through, int count, CancellationToken ct);
    Task<ConversationIndexOwner[]> PageAsync(Guid? after, int count, CancellationToken ct);
}

public interface IProjectIndexOwners
{
    Task<ProjectIndexOwner[]> ActivePageAsync(Guid? after, int count, CancellationToken ct);
}
