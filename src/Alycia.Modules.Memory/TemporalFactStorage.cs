using Alycia.Contracts.Memory;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Npgsql;

namespace Alycia.Modules.Memory;

// The host supplies the current session, including any transaction opened for a turn.
public sealed class TemporalFactStorage(DatabaseFacade database) : ITemporalFactStorage
{
    private const int Version = 1;
    public Task<int> ContiguousThroughAsync(Guid owner, int through, CancellationToken ct) =>
        database.SqlQuery<int>($"""
            SELECT CASE WHEN count(*) = coalesce(max(sequence),0) THEN coalesce(max(sequence),0) ELSE 0 END AS "Value"
            FROM chat.temporal_memory WHERE owner={owner} AND sequence BETWEEN 1 AND {through} AND version={Version}
            """).SingleAsync(ct);

    public Task<int[]> IndexedAsync(Guid owner, int[] sequences, CancellationToken ct) =>
        database.SqlQuery<int>($"""
            SELECT sequence AS "Value" FROM chat.temporal_memory WHERE owner={owner} AND sequence=ANY({sequences.ToArray()}) AND version={Version}
            """).ToArrayAsync(ct);

    public async Task SaveAsync(Guid owner, int sequence, DateTimeOffset observed, string[] topics, string facts, string search, CancellationToken ct)
    {
        try
        {
            await database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO chat.temporal_memory(owner,sequence,observed,topics,facts,search,version)
                VALUES ({owner},{sequence},{observed},{topics.ToArray()},{facts}::jsonb,to_tsvector('french',{search}),{Version})
                ON CONFLICT(owner,sequence) DO UPDATE SET topics=excluded.topics,facts=excluded.facts,search=excluded.search,version=excluded.version
                WHERE chat.temporal_memory.version <> excluded.version
                """, ct);
        }
        catch (PostgresException e) when (e.SqlState == PostgresErrorCodes.ForeignKeyViolation && e.ConstraintName == "temporal_memory_owner_fkey")
        {
            // The conversation was deleted between reading its immutable message and checkpointing it.
        }
    }

    public Task<int> CountAsync(Guid owner, int through, CancellationToken ct) =>
        database.SqlQuery<int>($"""SELECT count(*)::int AS "Value" FROM chat.temporal_memory WHERE owner={owner} AND sequence<={through} AND version={Version}""").SingleAsync(ct);
    public Task<List<string>> LexicalAsync(Guid owner, int through, DateTimeOffset before, string query, CancellationToken ct) =>
        database.SqlQuery<string>($"""
            SELECT facts::text AS "Value" FROM chat.temporal_memory WHERE owner={owner} AND observed<{before} AND sequence<={through} AND version={Version}
            ORDER BY ts_rank(search,websearch_to_tsquery('french',{query})) DESC, sequence DESC LIMIT 96
            """).ToListAsync(ct);
    public Task<List<string>> SelectedAsync(Guid owner, int through, DateTimeOffset before, int[] sequences, CancellationToken ct) =>
        database.SqlQuery<string>($"""
            SELECT facts::text AS "Value" FROM chat.temporal_memory WHERE owner={owner} AND observed<{before} AND sequence<={through} AND sequence=ANY({sequences.ToArray()}) AND version={Version}
            """).ToListAsync(ct);
    public Task<List<string>> TopicsAsync(Guid owner, int through, DateTimeOffset before, string[] topics, CancellationToken ct) =>
        database.SqlQuery<string>($"""
            SELECT facts::text AS "Value" FROM chat.temporal_memory WHERE owner={owner} AND observed<{before} AND sequence<={through} AND version={Version} AND topics && {topics.ToArray()}
            ORDER BY sequence LIMIT 257
            """).ToListAsync(ct);
    public Task<List<string>> ExportAsync(Guid owner, int after, int limit, CancellationToken ct) =>
        database.SqlQuery<string>($"""SELECT facts::text AS "Value" FROM chat.temporal_memory WHERE owner={owner} AND sequence>{after} ORDER BY sequence LIMIT {limit}""").ToListAsync(ct);
    public Task<int> ClearAsync(Guid owner, CancellationToken ct) =>
        database.ExecuteSqlInterpolatedAsync($"DELETE FROM chat.temporal_memory WHERE owner={owner}", ct);
}
