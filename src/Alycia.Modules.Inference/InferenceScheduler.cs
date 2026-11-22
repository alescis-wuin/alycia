namespace Alycia.Modules.Inference;

// Both transports share this singleton. Only one annotation may wait for the physical slot.
public sealed class InferenceScheduler
{
    private readonly SemaphoreSlim slot = new(1, 1);
    private readonly SemaphoreSlim annotations = new(1, 1);

    internal async Task<Lease> AcquireAsync(TimeSpan timeout, CancellationToken ct, bool annotation = false)
    {
        var admission = annotation ? annotations : null;
        if (admission is not null) await admission.WaitAsync(ct);
        var acquired = false;
        try
        {
            await slot.WaitAsync(ct);
            acquired = true;
            return new Lease(slot, admission, timeout, ct);
        }
        catch
        {
            if (acquired) slot.Release();
            admission?.Release();
            throw;
        }
    }

    internal sealed class Lease : IDisposable
    {
        private readonly SemaphoreSlim slot;
        private readonly SemaphoreSlim? admission;
        private readonly TimeSpan timeout;
        private readonly CancellationTokenSource deadline;
        private int disposed;

        public Lease(SemaphoreSlim slot, SemaphoreSlim? admission, TimeSpan timeout, CancellationToken ct)
        {
            this.slot = slot;
            this.admission = admission;
            this.timeout = timeout;
            deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
            RenewDeadline();
        }

        public CancellationToken Token => deadline.Token;
        public void RenewDeadline() => deadline.CancelAfter(timeout);

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0) return;
            deadline.Dispose();
            slot.Release();
            admission?.Release();
        }
    }
}
