using Alycia.Contracts.Documents;

namespace Alycia.Modules.Testing;

public sealed class MemoryFileChanges(IEnumerable<ConversationFileState> owners) : IConversationFileChanges
{
    private readonly Dictionary<Guid, ConversationFileState> _committed = owners.ToDictionary(x => x.Id);
    private readonly Dictionary<Guid, ConversationFileState> _pending = [];

    public Task<ConversationFileState> RequireAsync(Guid id, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(_committed.TryGetValue(id, out var state) ? state : throw new ConversationNotFoundException());
    }
    public async Task StageAdvanceAsync(Guid id, int expectedRevision, CancellationToken ct)
    {
        var saved = await RequireAsync(id, ct);
        var current = _pending.GetValueOrDefault(id, saved);
        if (current.Revision != expectedRevision) throw new ChatConflictException();
        _pending[id] = current with { Revision = current.Revision + 1 };
    }
    public void Commit()
    {
        foreach (var (key, state) in _pending) _committed[key] = state;
        _pending.Clear();
    }
}

public sealed class MemoryDocumentOverrides(IEnumerable<(Guid Conversation, Guid File)> initial) : IDocumentMembershipOverrides
{
    private readonly HashSet<(Guid Conversation, Guid File)> _modes = initial.ToHashSet();
    public bool Contains(Guid owner, Guid file) => _modes.Contains((owner, file));
    public Task ClearAsync(Guid conversation, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        _modes.RemoveWhere(m => m.Conversation == conversation);
        return Task.CompletedTask;
    }
}
