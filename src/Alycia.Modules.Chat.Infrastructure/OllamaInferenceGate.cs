namespace Alycia.Modules.Chat.Infrastructure;

// One generation/embedding request at a time in this API process.
// Does not control other clients or Ollama's model residency policy.
internal static class OllamaInferenceGate
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    public static async Task<IDisposable> EnterAsync(CancellationToken ct)
    {
        await Gate.WaitAsync(ct);
        return new Lease();
    }
    private sealed class Lease : IDisposable
    {
        private int _released;
        public void Dispose() { if (Interlocked.Exchange(ref _released, 1) == 0) Gate.Release(); }
    }
}
