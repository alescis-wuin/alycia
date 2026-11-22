using Alycia.Contracts.Chat;

namespace Alycia.Contracts.Context;

public sealed record PreparedContext(ContextWindow Context, string Memory, int Through, string SourceHash, bool Changed, string? LedgerJson = null)
{
    public PreparedMemoryChange? MemoryChange() => Changed ? new(Memory, Through, SourceHash, LedgerJson) : null;
}
