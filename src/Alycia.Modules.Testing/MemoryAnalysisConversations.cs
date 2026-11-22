using Alycia.Contracts.Documents;

namespace Alycia.Modules.Testing;

public sealed class MemoryAnalysisConversations(IEnumerable<(ConversationSummary State, AnalysisPreferences Settings)> owners) : IAnalysisConversation
{
    private readonly Dictionary<Guid, (ConversationSummary State, AnalysisPreferences Settings)> _owners = owners.ToDictionary(x => x.State.Id);
    private readonly Dictionary<Guid, ConversationSummary> _pending = [];

    public Task<AnalysisConversation> RequireAsync(Guid owner, int? revision, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!_owners.TryGetValue(owner, out var row)) throw new ConversationNotFoundException();
        if (revision.HasValue && revision != row.State.Revision) throw new ChatConflictException();
        return Task.FromResult(new AnalysisConversation(owner, row.State.Model, row.State.Revision));
    }
    public async Task<AnalysisPreferences> PreferencesAsync(Guid owner, int revision, CancellationToken ct)
    {
        await RequireAsync(owner, revision, ct);
        return _owners[owner].Settings;
    }
    public async Task<ConversationSummary> StageAdvanceAsync(Guid owner, int revision, CancellationToken ct)
    {
        await RequireAsync(owner, null, ct);
        var current = _pending.GetValueOrDefault(owner, _owners[owner].State);
        if (current.Revision != revision) throw new ChatConflictException();
        return _pending[owner] = current with { Revision = checked(revision + 1) };
    }
    public void Commit()
    {
        foreach (var (id, state) in _pending) _owners[id] = (state, _owners[id].Settings);
        _pending.Clear();
    }
}
