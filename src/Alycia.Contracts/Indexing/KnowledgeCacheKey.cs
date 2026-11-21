using System.Text.Json;
using Alycia.Contracts.Chat;

namespace Alycia.Contracts.Indexing;

public static class KnowledgeCacheKey
{
    public static string Compute(params string[] parts) => MemoryTransfer.Hash(JsonSerializer.Serialize(parts));
}
