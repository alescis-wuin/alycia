namespace Alycia.Modules.Chat.Infrastructure;

// The local chat and embedding transports share one active inference slot.
internal sealed class OllamaInferenceLease : IDisposable
{
    private static readonly SemaphoreSlim Slot = new(1, 1);
    private readonly CancellationTokenSource _deadline;
    private readonly TimeSpan _timeout;
    private int _disposed;

    private OllamaInferenceLease(TimeSpan timeout, CancellationToken ct)
    {
        _deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _timeout = timeout;
        RenewDeadline();
    }

    public CancellationToken Token => _deadline.Token;

    public static async Task<OllamaInferenceLease> AcquireAsync(TimeSpan timeout, CancellationToken ct)
    {
        // Queueing is cancellable by the caller. The execution deadline begins on acquisition.
        await Slot.WaitAsync(ct);
        try
        {
            return new OllamaInferenceLease(timeout, ct);
        }
        catch
        {
            Slot.Release();
            throw;
        }
    }

    // A streaming response renews its inactivity timer on every complete NDJSON line.
    public void RenewDeadline() => _deadline.CancelAfter(_timeout);

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            _deadline.Dispose();
            Slot.Release();
        }
    }
}
