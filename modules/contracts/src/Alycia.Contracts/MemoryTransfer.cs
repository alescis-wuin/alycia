using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Alycia.Contracts.Chat;

public static class MemoryTransfer
{
    public static readonly MemoryLedger Empty = new(1, 0, "", []);
    public static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    public static MemoryLedger ReadLedger(string json)
    {
        try
        {
            var ledger = JsonSerializer.Deserialize<MemoryLedger>(json, ChatJson.Options);
            return ledger is { Version: 1, Through: >= 0, Entries.Length: <= 256 } && ledger.Through % 2 == 0 ? ledger : Empty;
        }
        catch (JsonException) { return Empty; }
    }
}
