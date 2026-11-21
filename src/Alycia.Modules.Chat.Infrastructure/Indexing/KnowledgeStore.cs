using System.Text.Json;
using Alycia.Modules.Chat.Application;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace Alycia.Modules.Chat.Infrastructure;

// Independent writes checkpoint one immutable unit. Reads for chat use the caller's database snapshot.
public sealed class KnowledgeStore(ChatDbContext database, ILiveIndexFileAccess fileAccess, IIndexSourceDocuments documents) : IHistoryKnowledge
{
    private readonly string _connection = database.Database.GetConnectionString()!;
    public async Task PurgeExcludedAsync(CancellationToken ct)
    {
        await using var transaction = database.Database.CurrentTransaction is null
            ? await database.Database.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead, ct) : null;
        Guid? after = null;
        const int size = 128;
        while (true)
        {
            var ids = await documents.ExcludedPageAsync(after, size, ct);
            if (ids.Length > 0)
                await database.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM chat.knowledge_units WHERE file_id=ANY({ids})", ct);
            if (ids.Length < size) break;
            after = ids[^1];
        }
        if (transaction is not null) await transaction.CommitAsync(ct);
    }

    public Task<bool> AllowedAsync(Guid owner, bool project, Guid file, CancellationToken ct) =>
        fileAccess.IsAllowedAsync(owner, project, file, ct);

    public async Task<IndexedNote?> CachedAsync(string key, CancellationToken ct)
    {
        await using var db = new NpgsqlConnection(_connection);
        await db.OpenAsync(ct);
        await using var query = new NpgsqlCommand("SELECT payload::text FROM chat.knowledge_units WHERE cache_key=$1 ORDER BY indexed_at DESC LIMIT 1", db);
        query.Parameters.AddWithValue(key);
        return await query.ExecuteScalarAsync(ct) is string json ? Read(json) : null;
    }

    public async Task SaveAsync(Guid? file, Guid? conversation, IndexedNote unit, string cacheKey, CancellationToken ct)
    {
        await using var db = new NpgsqlConnection(_connection);
        await db.OpenAsync(ct);
        await using var write = new NpgsqlCommand("""
            INSERT INTO chat.knowledge_units(key,file_id,conversation_id,profile,source_hash,level,first_unit,last_unit,observed,payload,cache_key)
            VALUES($1,$2,$3,$4,$5,$6,$7,$8,$9,$10,$11)
            ON CONFLICT(key) DO UPDATE SET payload=excluded.payload,indexed_at=now()
            """, db);
        write.Parameters.AddWithValue(unit.Key);
        write.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Uuid, Value = (object?)file ?? DBNull.Value });
        write.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Uuid, Value = (object?)conversation ?? DBNull.Value });
        write.Parameters.AddWithValue(unit.Profile); write.Parameters.AddWithValue(unit.ContentHash); write.Parameters.AddWithValue(unit.Level);
        write.Parameters.AddWithValue(unit.First); write.Parameters.AddWithValue(unit.Last);
        write.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.TimestampTz, Value = (object?)unit.Observed ?? DBNull.Value });
        write.Parameters.AddWithValue(NpgsqlDbType.Jsonb, JsonSerializer.Serialize(unit, ProjectToolCatalog.Json));
        write.Parameters.AddWithValue(cacheKey);
        await write.ExecuteNonQueryAsync(ct);
    }

    // Only a published root is a retrieval entry point. Its children belong to that same generation.
    public static string DocumentProfile(string profile, bool hierarchical) => KnowledgeCacheKey.Compute(profile, hierarchical.ToString(), IndexedOriginal.StructureVersion.ToString(), StructuredDocuments.Version.ToString());
    public async Task<IndexedNote?> DocumentAsync(Guid file, string sha, string profile, CancellationToken ct, bool hierarchical = false)
    {
        profile = DocumentProfile(profile, hierarchical);
        var rows = await database.Database.SqlQuery<string>($"""
            SELECT payload::text AS "Value" FROM chat.knowledge_units
            WHERE file_id={file} AND profile={profile} AND source_hash={sha} AND level='document'
            ORDER BY indexed_at DESC LIMIT 1
            """).ToArrayAsync(ct);
        return rows.Length > 0 ? Read(rows[0]) : null;
    }

    public async Task<Dictionary<string, IndexedNote>> PassagesAsync(Guid file, IndexedNote root, CancellationToken ct)
    {
        var rows = await database.Database.SqlQuery<string>($"""
            WITH RECURSIVE tree AS (
                SELECT key,payload,level FROM chat.knowledge_units WHERE file_id={file} AND key={root.Key}
                UNION ALL
                SELECT c.key,c.payload,c.level FROM tree p CROSS JOIN LATERAL jsonb_array_elements_text(p.payload->'children') child(id)
                JOIN chat.knowledge_units c ON c.file_id={file} AND c.key=child.id
            ) SELECT payload::text AS "Value" FROM tree WHERE level='passage'
            """).ToArrayAsync(ct);
        return rows.Select(Read).DistinctBy(u => u.Labels.SourceUnitIds[0]).ToDictionary(u => u.Labels.SourceUnitIds[0]);
    }

    public async Task<IndexedNote?> ChildAsync(Guid file, IndexedNote root, string key, CancellationToken ct)
    {
        var rows = await database.Database.SqlQuery<string>($"""
            WITH RECURSIVE tree AS (
                SELECT key,payload FROM chat.knowledge_units WHERE file_id={file} AND key={root.Key}
                UNION
                SELECT c.key,c.payload FROM tree p CROSS JOIN LATERAL jsonb_array_elements_text(p.payload->'children') child(id)
                JOIN chat.knowledge_units c ON c.file_id={file} AND c.key=child.id
            ) SELECT payload::text AS "Value" FROM tree WHERE key={key} LIMIT 1
            """).ToArrayAsync(ct);
        return rows.Length == 0 ? null : Read(rows[0]);
    }

    public async Task<IndexedNote[]> HistoryPageAsync(Guid owner, string profile, int before, CancellationToken ct)
    {
        var rows = await database.Database.SqlQuery<string>($"""
            SELECT payload::text AS "Value" FROM (
                SELECT DISTINCT ON (first_unit,payload->>'role') last_unit,payload
                FROM chat.knowledge_units WHERE conversation_id={owner} AND profile={profile} AND level='history-group'
                ORDER BY first_unit,payload->>'role',last_unit DESC,indexed_at DESC
            ) groups WHERE last_unit<{before} ORDER BY last_unit DESC LIMIT 24
            """).ToListAsync(ct);
        return rows.Select(Read).ToArray();
    }

    public async Task<IndexedNote[]> HistoryChildrenAsync(Guid owner, string profile, string key, CancellationToken ct)
    {
        var rows = await database.Database.SqlQuery<string>($"""
            SELECT child.payload::text AS "Value" FROM chat.knowledge_units parent
            CROSS JOIN LATERAL jsonb_array_elements_text(parent.payload->'children') item(id)
            JOIN chat.knowledge_units child ON child.key=item.id AND child.conversation_id={owner} AND child.profile={profile} AND child.level='message'
            WHERE parent.key={key} AND parent.conversation_id={owner} AND parent.profile={profile} AND parent.level='history-group'
            ORDER BY child.first_unit
            """).ToListAsync(ct);
        return rows.Select(Read).ToArray();
    }

    public async Task<Dictionary<int, double>> HistoryAsync(Guid owner, string question, string profile, int through, DateTimeOffset before, CancellationToken ct)
    {
        var terms = string.Join(" | ", LexicalTerms.Split(question).Distinct().Take(32));
        if (terms.Length == 0) return [];
        var rows = await database.Database.SqlQuery<string>($"""
            WITH query AS (SELECT to_tsquery('simple'::regconfig,{terms}) q)
            SELECT candidate.payload::text AS "Value" FROM chat.knowledge_units candidate,query
            WHERE conversation_id={owner} AND profile={profile} AND level='history-group' AND search_terms @@ query.q
              AND last_unit<={through} AND observed<{before}
              AND NOT EXISTS (SELECT 1 FROM chat.knowledge_units newer WHERE newer.conversation_id=candidate.conversation_id
                  AND newer.profile=candidate.profile AND newer.level=candidate.level AND newer.first_unit=candidate.first_unit
                  AND newer.payload->>'role'=candidate.payload->>'role' AND newer.last_unit<={through} AND newer.observed<{before}
                  AND (newer.last_unit,newer.indexed_at)>(candidate.last_unit,candidate.indexed_at))
            ORDER BY ts_rank(search_terms,query.q) DESC,last_unit DESC,key LIMIT 64
            """).ToListAsync(ct);
        var groups = rows.Select(Read).ToArray();
        var chunks = groups.Select(g => new SourceChunk(owner, "", g.Key, g.Labels.Title, "history-group", 0, 0, 0, 0,
            g.Labels.Summary + " " + IndexedKnowledge.Terms(g.Labels), [], false)).ToArray();
        var scores = ProjectRetrieval.Bm25(chunks, question);
        var result = new Dictionary<int, double>();
        foreach (var (n, rank) in Enumerable.Range(0, groups.Length).Where(n => scores[n] > 0).OrderByDescending(n => scores[n]).Take(4).Select((index, rank) => (index, rank)))
            foreach (var reference in groups[n].Labels.SourceUnitIds)
            {
                var colon = reference.IndexOf(':');
                if (colon > 1 && reference.StartsWith('H') && int.TryParse(reference.AsSpan(1, colon - 1), out var seq))
                    result.TryAdd(seq, 1.0 / (1 + rank));
            }
        return result;
    }

    private static IndexedNote Read(string json) => JsonSerializer.Deserialize<IndexedNote>(json, ProjectToolCatalog.Json)!;
}
