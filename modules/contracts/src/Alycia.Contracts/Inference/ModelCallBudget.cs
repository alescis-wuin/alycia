namespace Alycia.Contracts.Inference;

// Atomic reservations precede model calls. All stages of the operation share the same allowance.
public sealed class ModelCallBudget(int maximum)
{
    private readonly int _capacity = maximum;
    private int _remaining = maximum;
    public int Remaining => Volatile.Read(ref _remaining);
    public int Used => _capacity - Remaining;
    public bool TryReserve()
    {
        int remaining;
        do { remaining = Volatile.Read(ref _remaining); if (remaining == 0) return false; }
        while (Interlocked.CompareExchange(ref _remaining, remaining - 1, remaining) != remaining);
        return true;
    }
}
