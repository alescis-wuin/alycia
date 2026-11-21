namespace Alycia.Contracts.Workspaces;

public sealed record WorkspaceIdentity(Guid Id, string Name);

public interface IWorkspaceIdentityDirectory
{
    Task<IReadOnlyList<WorkspaceIdentity>> ListAsync(CancellationToken ct);
    Task<bool> ExistsAsync(Guid id, CancellationToken ct);
    Task<WorkspaceIdentity> CreateAsync(string name, CancellationToken ct);
}
