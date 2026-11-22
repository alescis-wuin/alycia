using Alycia.Modules.Chat.Application;
using Alycia.Modules.Chat.Domain;
using Alycia.Contracts.Memory;

namespace Alycia.Modules.Chat.Tests.Doubles;

// One instance models one scoped unit of work. Rollback restores its owner snapshot and releases staged messages.
public sealed class MemoryChatRepository : IChatRepository
{
    private readonly Dictionary<Guid, Conversation> _rows = [];
    private readonly Dictionary<Guid, Conversation> _tracked = [];
    private readonly List<ChatMessage> _messages = [];
    public Task<IChatTurnScope?> BeginTurnAsync(Guid id, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult<IChatTurnScope?>(_rows.GetValueOrDefault(id)?.WorkspaceId is not null
            ? new Scope(this, id, _rows[id].DetachedCopy(), _messages.Where(m => m.ConversationId == id).ToArray()) : null);
    }
    private sealed class Scope(MemoryChatRepository db, Guid id, Conversation before, ChatMessage[] previous) : IChatTurnScope
    {
        private bool _committed;
        public Task CommitAsync(CancellationToken ct) { ct.ThrowIfCancellationRequested(); _committed = true; return Task.CompletedTask; }
        public ValueTask DisposeAsync()
        {
            if (!_committed)
            {
                db._rows[id] = before;
                db._tracked.Remove(id);
                db._messages.RemoveAll(m => m.ConversationId == id);
                db._messages.AddRange(previous);
            }
            return ValueTask.CompletedTask;
        }
    }
    public Task<Conversation?> FindAsync(Guid id, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!_rows.TryGetValue(id, out var row)) return Task.FromResult<Conversation?>(null);
        if (!_tracked.TryGetValue(id, out var tracked)) _tracked[id] = tracked = row.DetachedCopy();
        return Task.FromResult<Conversation?>(tracked);
    }
    public Task<IReadOnlyList<Conversation>> ListAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<Conversation>>(_rows.Values.OrderByDescending(c => c.UpdatedAtUtc).ThenBy(c => c.Id).Select(c => c.DetachedCopy()).ToArray());
    }
    public Task AddAsync(Conversation c, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!_rows.TryAdd(c.Id, c.DetachedCopy())) throw new ChatConflictException();
        _tracked[c.Id] = c;
        return Task.CompletedTask;
    }
    public Task SaveAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        foreach (var (id, c) in _tracked) _rows[id] = c.DetachedCopy();
        return Task.CompletedTask;
    }
    public Task DeleteAsync(Conversation c, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested(); _tracked.Remove(c.Id); _rows.Remove(c.Id); _messages.RemoveAll(m => m.ConversationId == c.Id);
        return Task.CompletedTask;
    }
    public Task<IReadOnlyList<ChatMessage>> MessagesAsync(Guid id, int limit, int? before, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<ChatMessage>>(_messages.Where(m => m.ConversationId == id && (before == null || m.Sequence < before)).OrderBy(m => m.Sequence).TakeLast(limit).ToArray());
    }
    public Task<IReadOnlyList<ChatMessage>> FindTurnAsync(Guid id, Guid requestId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<ChatMessage>>(_messages.Where(m => m.ConversationId == id && m.RequestId == requestId).OrderBy(m => m.Sequence).ToArray());
    }
    public async Task AppendTurnAsync(Conversation c, ChatMessage user, ChatMessage assistant, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (user.ConversationId != c.Id || assistant.ConversationId != c.Id || user.RequestId != assistant.RequestId ||
            user.Role != "user" || assistant.Role != "assistant" || assistant.Sequence != user.Sequence + 1) throw new ArgumentException("Tour incompatible.");
        if (_messages.Any(m => m.ConversationId == c.Id && (m.RequestId == user.RequestId || m.Sequence == user.Sequence || m.Sequence == assistant.Sequence))) throw new ChatConflictException();
        _messages.AddRange([user, assistant]); await SaveAsync(ct);
    }
}

public sealed class FixtureConversationMemory(IChatRepository repository) : IConversationMemory
{
    public bool Apply(Conversation c, PreparedMemoryChange? change)
    {
        if (change is null) return false;
        if (change.LedgerJson is { } ledger) c.SetMemoryLedger(ledger);
        else c.SetMemory(change.Memory, change.Through, change.SourceHash, c.Model);
        return true;
    }
    public async Task<Conversation> ResetAsync(Guid id, int revision, CancellationToken ct)
    {
        var conversation = await repository.FindAsync(id, ct) ?? throw new ConversationNotFoundException();
        if (conversation.Revision != revision) throw new ChatConflictException();
        conversation.SetMemory("", 0, "", ""); conversation.SetMemoryLedger("{}"); await repository.SaveAsync(ct); return conversation;
    }
}
