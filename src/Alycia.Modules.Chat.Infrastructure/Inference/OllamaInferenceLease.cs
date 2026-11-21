namespace Alycia.Modules.Chat.Infrastructure;

// The local chat and embedding transports share one active inference slot.
internal sealed class OllamaInferenceLease : IDisposable
{
    private static readonly SemaphoreSlim Slot = new(1, 1);
    // A pass can prepare several files in parallel. Admit only one annotation to the
    // shared slot so already prepared jobs cannot all queue ahead of a new chat.
    private static readonly SemaphoreSlim AnnotationAdmission = new(1, 1);
    private readonly SemaphoreSlim? _admission;
    private readonly CancellationTokenSource _deadline;
    private readonly TimeSpan _timeout;
    private int _disposed;

    private OllamaInferenceLease(TimeSpan timeout, CancellationToken ct, SemaphoreSlim? admission)
    {
        _deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _timeout = timeout;
        _admission = admission;
        RenewDeadline();
    }

    public CancellationToken Token => _deadline.Token;

    public static async Task<OllamaInferenceLease> AcquireAsync(TimeSpan timeout, CancellationToken ct, bool annotation = false)
    {
        // Queueing is cancellable by the caller. The execution deadline begins on acquisition.
        var admission = annotation ? AnnotationAdmission : null;
        if (admission is not null) await admission.WaitAsync(ct);
        var hasSlot = false;
        try
        {
            await Slot.WaitAsync(ct);
            hasSlot = true;
            return new OllamaInferenceLease(timeout, ct, admission);
        }
        catch
        {
            if (hasSlot) Slot.Release();
            admission?.Release();
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
            _admission?.Release();
        }
    }
}
