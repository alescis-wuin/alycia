namespace Alycia.Modules.Inference;

public sealed class ForegroundActivity : IForegroundActivity
{
    private int _active;
    public bool ForegroundRequested => Volatile.Read(ref _active) > 0;
    public IDisposable PreferForeground()
    {
        Interlocked.Increment(ref _active);
        return new Lease(this);
    }

    private sealed class Lease(ForegroundActivity owner) : IDisposable
    {
        private ForegroundActivity? _owner = owner;
        public void Dispose()
        {
            var target = Interlocked.Exchange(ref _owner, null);
            if (target is not null) Interlocked.Decrement(ref target._active);
        }
    }
}
