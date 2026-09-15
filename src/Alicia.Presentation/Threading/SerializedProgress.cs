namespace Alicia.Presentation.Threading;

internal sealed class SerializedProgress<T> : IProgress<T>
{
    private readonly object _gate = new();
    private readonly Action<T> _handler;
    private readonly Queue<T> _pending = new();
    private readonly SynchronizationContext? _synchronizationContext;
    private bool _drainScheduled;

    public SerializedProgress(Action<T> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);

        _handler = handler;
        _synchronizationContext = SynchronizationContext.Current;
    }

    public void Report(T value)
    {
        bool shouldScheduleDrain;

        lock (_gate)
        {
            _pending.Enqueue(value);
            shouldScheduleDrain = !_drainScheduled;
            _drainScheduled = true;
        }

        if (!shouldScheduleDrain)
        {
            return;
        }

        if (_synchronizationContext is null
            || ReferenceEquals(SynchronizationContext.Current, _synchronizationContext))
        {
            Drain();
            return;
        }

        _synchronizationContext.Post(
            static state => ((SerializedProgress<T>)state!).Drain(),
            this);
    }

    private void Drain()
    {
        while (TryDequeue(out T value))
        {
            _handler(value);
        }
    }

    private bool TryDequeue(out T value)
    {
        lock (_gate)
        {
            if (_pending.Count == 0)
            {
                _drainScheduled = false;
                value = default!;
                return false;
            }

            value = _pending.Dequeue();
            return true;
        }
    }
}
