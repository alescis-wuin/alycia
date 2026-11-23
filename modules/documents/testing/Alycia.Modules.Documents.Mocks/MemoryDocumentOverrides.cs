using Alycia.Contracts.Documents;

namespace Alycia.Modules.Testing;

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
