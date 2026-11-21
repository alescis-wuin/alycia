using Alycia.Contracts.Documents;

namespace Alycia.Modules.Testing;

public sealed class SnapshotConversations(IEnumerable<ConversationIndexOwner> owners, IEnumerable<IndexHistoryMessage> history)
    : IConversationIndexOwners, IToolConversationPolicy, IConversationWorkspaceLookup
{
    private readonly ConversationIndexOwner[] _owners = owners.OrderBy(c => c.Id).ToArray();
    private readonly IndexHistoryMessage[] _messages = history.OrderBy(m => m.Sequence).ToArray();

    public Task<ConversationIndexOwner?> FindAsync(Guid id, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(_owners.SingleOrDefault(c => c.Id == id));
    }

    public async Task<Guid?> FindWorkspaceAsync(Guid id, CancellationToken ct) => (await FindAsync(id, ct))?.WorkspaceId;

    public async Task<ToolConversationPolicy?> ReadAsync(Guid id, CancellationToken ct) =>
        await FindAsync(id, ct) is { } c ? new(c.SettingsJson, c.MessageCount) : null;

    public Task<bool> HasHistoryAsync(Guid id, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(_messages.Any(m => m.ConversationId == id));
    }

    public Task<ConversationIndexOwner[]> PageAsync(Guid? after, int count, CancellationToken ct)
    {
        if (count is < 1 or > 512) throw new ArgumentOutOfRangeException(nameof(count));
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(_owners.Where(o => !after.HasValue || o.Id.CompareTo(after.Value) > 0).Take(count).ToArray());
    }

    public Task<IndexHistoryMessage[]> HistoryPageAsync(Guid owner, int after, int through, int count, CancellationToken ct)
    {
        if (count is < 1 or > 128) throw new ArgumentOutOfRangeException(nameof(count));
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(_messages.Where(m => m.ConversationId == owner && m.Sequence > after && m.Sequence <= through)
            .Take(count).ToArray());
    }
}

public sealed class SnapshotProjects(IEnumerable<ProjectIndexOwner> active) : IProjectIndexOwners
{
    private readonly ProjectIndexOwner[] _active = active.OrderBy(p => p.Id).ToArray();
    public Task<ProjectIndexOwner[]> ActivePageAsync(Guid? after, int count, CancellationToken ct)
    {
        if (count is < 1 or > 512) throw new ArgumentOutOfRangeException(nameof(count));
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(_active.Where(p => !after.HasValue || p.Id.CompareTo(after.Value) > 0).Take(count).ToArray());
    }
}
