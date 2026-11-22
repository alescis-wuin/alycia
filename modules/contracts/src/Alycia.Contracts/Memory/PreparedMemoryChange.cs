namespace Alycia.Contracts.Chat;

public sealed record PreparedMemoryChange(string Memory, int Through, string SourceHash, string? LedgerJson);
