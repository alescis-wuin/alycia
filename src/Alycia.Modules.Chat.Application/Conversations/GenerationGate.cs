using System.Collections.Concurrent;

namespace Alycia.Modules.Chat.Application;

// One active inference per conversation in this API process. The database
// revision remains the authoritative cross-process protection at commit time.
public sealed class GenerationGate
{
    private readonly ConcurrentDictionary<Guid, byte> _active = new();

    public IDisposable Enter(Guid id)
    {
        if (!_active.TryAdd(id, 0)) throw new ChatConflictException();
        return new Lease(() => _active.TryRemove(id, out _));
    }

    private sealed class Lease(Action release) : IDisposable
    {
        private Action? _release = release;
        public void Dispose() => Interlocked.Exchange(ref _release, null)?.Invoke();
    }
}
