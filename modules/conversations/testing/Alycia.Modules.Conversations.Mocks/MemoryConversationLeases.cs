using Alycia.Contracts.Documents;

namespace Alycia.Modules.Testing;

public sealed class MemoryConversationLeases : IConversationLease
{
    private readonly HashSet<Guid> _active = [];

    public IDisposable Enter(Guid id)
    {
        lock (_active)
        {
            if (!_active.Add(id)) throw new ChatConflictException();
            return new Reservation(this, id);
        }
    }

    private sealed class Reservation(MemoryConversationLeases owner, Guid id) : IDisposable
    {
        private bool _released;
        public void Dispose()
        {
            lock (owner._active)
            {
                if (_released) return;
                _released = true;
                owner._active.Remove(id);
            }
        }
    }
}
