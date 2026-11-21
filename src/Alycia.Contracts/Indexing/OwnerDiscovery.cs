namespace Alycia.Contracts.Indexing;

public sealed record ConversationIndexOwner(Guid Id, Guid? WorkspaceId, int Revision, string SettingsJson);
public sealed record ProjectIndexOwner(Guid Id, int Revision);

public interface IConversationIndexOwners
{
    Task<ConversationIndexOwner[]> PageAsync(Guid? after, int count, CancellationToken ct);
}

public interface IProjectIndexOwners
{
    Task<ProjectIndexOwner[]> ActivePageAsync(Guid? after, int count, CancellationToken ct);
}
