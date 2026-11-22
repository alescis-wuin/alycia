using System.Runtime.CompilerServices;
using Alycia.Contracts.Indexing;
using Npgsql;

namespace Alycia.Modules.Memory;

// The database reader holds a single statement snapshot across all pages.
public sealed class MemoryVectorEligibility(string connectionString) : IMemoryVectorEligibility
{
    public async IAsyncEnumerable<MemoryVectorSource[]> PagesAsync(Guid owner, int through, DateTimeOffset before,
        [EnumeratorCancellation] CancellationToken ct)
    {
        await using var db = new NpgsqlConnection(connectionString);
        await db.OpenAsync(ct);
        await using var query = new NpgsqlCommand("""
            SELECT sequence, facts->0->>'sha256' FROM chat.temporal_memory
            WHERE owner=$1 AND sequence<=$2 AND observed<$3 AND facts->0->>'sha256' IS NOT NULL ORDER BY sequence
            """, db);
        query.Parameters.AddWithValue(owner);
        query.Parameters.AddWithValue(through);
        query.Parameters.AddWithValue(before);
        await using var rows = await query.ExecuteReaderAsync(ct);
        var page = new List<MemoryVectorSource>(128);
        while (await rows.ReadAsync(ct))
        {
            page.Add(new(rows.GetInt32(0), rows.GetString(1)));
            if (page.Count != 128) continue;
            yield return page.ToArray();
            page.Clear();
        }
        if (page.Count != 0) yield return page.ToArray();
    }
}
