using System.Data.Common;
using System.Text.Json;
using Npgsql;
using NpgsqlTypes;

namespace Alycia.Modules.Indexing;

public sealed record EmbeddingSettings(string Model, int BatchSize, int ChunkCharacters, bool History, string SourceRevision = "", bool Hierarchical = false, string DocumentPrefix = "", string QueryPrefix = "", EnrichmentOptions? Enrichment = null)
{
    public static bool RequiresJob(IIndexSettings options) =>
        options.Enrichment.Enabled || options.RetrievalMode == "hybrid" && !string.IsNullOrWhiteSpace(options.EmbeddingModel);

    public static EmbeddingSettings From(IIndexSettings options) =>
        Capture(options, options.RetrievalMode == "hybrid" || !options.Enrichment.Enabled ? options.EmbeddingModel : "");

    // A direct query uses the selected encoder even when background jobs only perform enrichment.
    public static EmbeddingSettings ForQuery(IIndexSettings options) => Capture(options, options.EmbeddingModel);

    private static EmbeddingSettings Capture(IIndexSettings options, string model) => new(
        model, options.EmbeddingBatchSize, options.EmbeddingChunkCharacters, options.EmbedHistory, Hierarchical: options.HierarchicalIndexEnabled,
        DocumentPrefix: options.EmbeddingDocumentPrefix, QueryPrefix: options.EmbeddingQueryPrefix, Enrichment: options.Enrichment.Enabled ? options.Enrichment : null);
    public bool Versioned => Hierarchical || DocumentPrefix.Length > 0 || QueryPrefix.Length > 0;
    public string VectorSpace(string digest, int dimensions) => !Versioned ? digest : digest + ":pipeline-v4:" + dimensions + ":"
        + MemoryTransfer.Hash(JsonSerializer.Serialize(new { Hierarchical, ChunkCharacters, DocumentPrefix, QueryPrefix, Normalization = "cosine" }));
}
public sealed record EmbeddingJob(EmbeddingProgress Progress, EmbeddingSettings Settings, int Revision, string Fingerprint, DateTimeOffset? RetryAt = null, string? VectorDigest = null, int? VectorDimensions = null)
{
    public bool IsDue(DateTimeOffset now) => Progress.State is not ("failed" or "paused")
        && (Progress.State != "completed" || now - Progress.UpdatedAt >= TimeSpan.FromSeconds(30));
}

// A separate short-lived connection avoids writing indexing state into a chat's repeatable-read snapshot.
public sealed class PostgresEmbeddingStore(string connectionString, IMemoryVectorEligibility memory, Func<DbTransaction, IndexJobSource> sourceFor) : IEmbeddingSimilarityReader
{
    private async Task<NpgsqlConnection> OpenAsync(CancellationToken ct)
    {
        var connection = new NpgsqlConnection(connectionString);
        try { await connection.OpenAsync(ct); return connection; }
        catch { await connection.DisposeAsync(); throw; }
    }

    public async Task QueueAsync(Guid owner, bool project, EmbeddingSettings profile, CancellationToken ct, bool retry = false)
    {
        await using var connection = await OpenAsync(ct);
        await using var command = new NpgsqlCommand("""
            INSERT INTO chat.embedding_jobs(owner_id, is_project, options_json) VALUES ($1, $2, $3)
            ON CONFLICT(owner_id, is_project) DO UPDATE SET options_json = EXCLUDED.options_json,
                revision = chat.embedding_jobs.revision + 1, retry_at = CASE WHEN $4 THEN now() ELSE chat.embedding_jobs.retry_at END,
                state = CASE WHEN chat.embedding_jobs.state = 'paused' AND NOT $4 THEN 'paused' ELSE 'queued' END,
                detail = '', fingerprint = '', cycle_calls = 0, updated_at = now()
            WHERE chat.embedding_jobs.options_json <> EXCLUDED.options_json OR $4
            """, connection);
        command.Parameters.AddWithValue(owner); command.Parameters.AddWithValue(project);
        command.Parameters.AddWithValue(JsonSerializer.Serialize(profile, ProjectToolCatalog.Json));
        command.Parameters.AddWithValue(retry);
        await command.ExecuteNonQueryAsync(ct);
    }

    public async Task<EmbeddingJob[]> JobsAsync(CancellationToken ct)
    {
        await using var connection = await OpenAsync(ct);
        await using var command = new NpgsqlCommand("SELECT owner_id, is_project, state, completed, total, detail, options_json, updated_at, revision, fingerprint,retry_at,cycle_calls,vector_digest,vector_dimensions FROM chat.embedding_jobs ORDER BY updated_at", connection);
        await using var reader = await command.ExecuteReaderAsync(ct);
        var jobs = new List<EmbeddingJob>();
        while (await reader.ReadAsync(ct))
        {
            var settings = JsonSerializer.Deserialize<EmbeddingSettings>(reader.GetString(6), ProjectToolCatalog.Json)!;
            jobs.Add(new(new(reader.GetGuid(0), reader.GetBoolean(1), reader.GetString(2), reader.GetInt32(3),
                reader.GetInt32(4), reader.GetString(5), settings.Model, reader.GetFieldValue<DateTimeOffset>(7), CycleCalls: reader.GetInt32(11)),
                settings, reader.GetInt32(8), reader.GetString(9), reader.IsDBNull(10) ? null : reader.GetFieldValue<DateTimeOffset>(10),
                reader.IsDBNull(12) ? null : reader.GetString(12), reader.IsDBNull(13) ? null : reader.GetInt32(13)));
        }
        return jobs.Select(j => j with
        {
            Progress = j.Progress with
            {
                BatchSize = j.Settings.BatchSize,
                ChunkCharacters = j.Settings.ChunkCharacters,
                Hierarchical = j.Settings.Hierarchical,
                DocumentPrefix = j.Settings.DocumentPrefix,
                QueryPrefix = j.Settings.QueryPrefix,
                Enrichment = j.Settings.Enrichment
            }
        }).ToArray();
    }

    public async Task<bool> ActiveAsync(EmbeddingJob job, CancellationToken ct)
    {
        await using var connection = await OpenAsync(ct);
        await using var snapshot = await connection.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead, ct);
        await using var query = new NpgsqlCommand("""
            SELECT EXISTS(SELECT 1 FROM chat.embedding_jobs WHERE owner_id=$1 AND is_project=$2 AND revision=$3 AND state<>'paused')
            """, connection, snapshot);
        query.Parameters.AddWithValue(job.Progress.OwnerId);
        query.Parameters.AddWithValue(job.Progress.IsProject);
        query.Parameters.AddWithValue(job.Revision);
        if (await query.ExecuteScalarAsync(ct) is not true) return false;
        return job.Settings.SourceRevision.Length == 0 ||
            await sourceFor(snapshot).RevisionAsync(job.Progress.OwnerId, job.Progress.IsProject, ct) == job.Settings.SourceRevision;
    }

    public async Task SetDimensionsAsync(EmbeddingJob job, string digest, int dimensions, CancellationToken ct)
    {
        await using var connection = await OpenAsync(ct);
        await using var command = new NpgsqlCommand("UPDATE chat.embedding_jobs SET vector_digest=$1, vector_dimensions=$2 WHERE owner_id=$3 AND is_project=$4 AND revision=$5", connection);
        command.Parameters.AddWithValue(digest); command.Parameters.AddWithValue(dimensions); command.Parameters.AddWithValue(job.Progress.OwnerId);
        command.Parameters.AddWithValue(job.Progress.IsProject); command.Parameters.AddWithValue(job.Revision);
        await command.ExecuteNonQueryAsync(ct);
    }

    public async Task PauseAsync(Guid owner, bool project, CancellationToken ct)
    {
        await using var connection = await OpenAsync(ct);
        await using var command = new NpgsqlCommand("UPDATE chat.embedding_jobs SET state = 'paused', revision = revision + 1, updated_at = now() WHERE owner_id = $1 AND is_project = $2", connection);
        command.Parameters.AddWithValue(owner); command.Parameters.AddWithValue(project);
        await command.ExecuteNonQueryAsync(ct);
    }

    public async Task<bool> ProgressAsync(EmbeddingJob job, string state, int completed, int total, string detail,
        string fingerprint, CancellationToken ct, int? cycleCalls = null)
    {
        await using var connection = await OpenAsync(ct);
        await using var command = new NpgsqlCommand("""
            UPDATE chat.embedding_jobs SET state = $1, completed = $2, total = $3, detail = $4, fingerprint = $5, updated_at = now(), cycle_calls=coalesce($9,cycle_calls)
            WHERE owner_id = $6 AND is_project = $7 AND revision = $8
            """, connection);
        command.Parameters.AddWithValue(state); command.Parameters.AddWithValue(completed); command.Parameters.AddWithValue(total);
        command.Parameters.AddWithValue(detail); command.Parameters.AddWithValue(fingerprint);
        command.Parameters.AddWithValue(job.Progress.OwnerId); command.Parameters.AddWithValue(job.Progress.IsProject);
        command.Parameters.AddWithValue(job.Revision);
        command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Integer, Value = (object?)cycleCalls ?? DBNull.Value });
        return await command.ExecuteNonQueryAsync(ct) == 1;
    }

    public async Task DeleteJobAsync(EmbeddingJob job, CancellationToken ct)
    {
        await using var connection = await OpenAsync(ct);
        await using var command = new NpgsqlCommand("DELETE FROM chat.embedding_jobs WHERE owner_id = $1 AND is_project = $2", connection);
        command.Parameters.AddWithValue(job.Progress.OwnerId); command.Parameters.AddWithValue(job.Progress.IsProject);
        await command.ExecuteNonQueryAsync(ct);
    }

    public async Task<IAsyncDisposable?> TryLockAsync(EmbeddingJob job, CancellationToken ct)
    {
        var connection = new NpgsqlConnection(new NpgsqlConnectionStringBuilder(connectionString) { Pooling = false }.ToString());
        await connection.OpenAsync(ct);
        await using var command = new NpgsqlCommand("SELECT pg_try_advisory_lock(hashtextextended($1, 0))", connection);
        command.Parameters.AddWithValue("alycia-embeddings:" + job.Progress.OwnerId + ":" + job.Progress.IsProject);
        try
        {
            if (await command.ExecuteScalarAsync(ct) is true) return connection;
            await connection.DisposeAsync(); return null;
        }
        catch { await connection.DisposeAsync(); throw; }
    }

    public async Task<Dictionary<string, float[]>> ReadAsync(string identity, IReadOnlyList<string> keys, CancellationToken ct)
    {
        var values = new Dictionary<string, float[]>(StringComparer.Ordinal);
        if (keys.Count == 0) return values;
        await using var connection = await OpenAsync(ct);
        foreach (var batch in keys.Chunk(256))
        {
            await using var command = new NpgsqlCommand("SELECT key, embedding FROM chat.embedding_vectors WHERE identity = $1 AND key = ANY($2)", connection);
            command.Parameters.AddWithValue(identity); command.Parameters.AddWithValue(batch);
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct)) values[reader.GetString(0)] = reader.GetFieldValue<float[]>(1);
        }
        return values;
    }

    public async Task WriteAsync(string identity, SourceChunk chunk, float[] vector, Guid? conversation, int sequence, CancellationToken ct)
    {
        if (vector.Length is < 1 or > 8192 || vector.Any(v => !float.IsFinite(v))) throw new ChatModelException("Vecteur invalide.");
        await using var connection = await OpenAsync(ct);
        await using var command = new NpgsqlCommand("""
            INSERT INTO chat.embedding_vectors(identity, key, file_id, conversation_id, sequence, start_offset, end_offset, content_hash, embedding)
            VALUES ($1,$2,$3,$4,$5,$6,$7,$8,$9)
            ON CONFLICT(identity,key) DO UPDATE SET embedding = EXCLUDED.embedding
            """, connection);
        command.Parameters.AddWithValue(identity); command.Parameters.AddWithValue(EmbeddingChunks.Key(chunk));
        command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Uuid, Value = conversation is null ? chunk.FileId : DBNull.Value });
        command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Uuid, Value = conversation is Guid id ? id : DBNull.Value });
        command.Parameters.AddWithValue(sequence); command.Parameters.AddWithValue(chunk.Start); command.Parameters.AddWithValue(chunk.End);
        command.Parameters.AddWithValue(chunk.Sha256); command.Parameters.AddWithValue(vector);
        await command.ExecuteNonQueryAsync(ct);
    }

    public async Task<Dictionary<int, double>> MemorySimilaritiesAsync(Guid owner, string identity, int through, DateTimeOffset before,
        float[] query, CancellationToken ct)
    {
        await using var connection = await OpenAsync(ct);
        await using var ext = new NpgsqlCommand("SELECT EXISTS(SELECT 1 FROM pg_extension WHERE extname='vector')", connection);
        var pgvector = await ext.ExecuteScalarAsync(ct) is true;
        // A page contributes at most 32 finalists. Sequence descending breaks ties across pages as in PostgreSQL.
        const string fromEligible = """
             FROM chat.embedding_vectors v JOIN unnest($3::int[], $4::text[]) AS source(seq, hash)
             ON v.sequence=source.seq AND v.content_hash=source.hash
             WHERE v.conversation_id=$1 AND v.identity=$2 AND cardinality(v.embedding)=cardinality($5::real[])
             GROUP BY v.sequence ORDER BY similarity DESC NULLS LAST, v.sequence DESC LIMIT 32
            """;
        const string vectorSql = "SELECT v.sequence, max(1 - (v.embedding::vector <=> $5::real[]::vector)) AS similarity " + fromEligible;
        const string arraySql = """
            SELECT v.sequence, max((SELECT sum(x::float8 * y) / nullif(sqrt(sum(x::float8*x) * sum(y::float8*y)),0)
                FROM unnest(v.embedding,$5::real[]) AS pairs(x,y))) AS similarity
            """ + " " + fromEligible;
        var best = new List<(int Sequence, double Score)>();
        await foreach (var page in memory.PagesAsync(owner, through, before, ct))
        {
            await using var cmd = pgvector ? new NpgsqlCommand(vectorSql, connection) : new NpgsqlCommand(arraySql, connection);
            cmd.Parameters.AddWithValue(owner);
            cmd.Parameters.AddWithValue(identity);
            cmd.Parameters.AddWithValue(page.Select(p => p.Sequence).ToArray());
            cmd.Parameters.AddWithValue(page.Select(p => p.Sha256).ToArray());
            cmd.Parameters.AddWithValue(query);
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
                if (!reader.IsDBNull(1)) best.Add((reader.GetInt32(0), reader.GetDouble(1)));
            best = best.OrderByDescending(r => r.Score).ThenByDescending(r => r.Sequence).Take(32).ToList();
        }
        return best.ToDictionary(r => r.Sequence, r => r.Score);
    }

    public async Task<Dictionary<string, double>> SimilaritiesAsync(string identity, string[] keys, float[] query, CancellationToken ct)
    {
        await using var connection = await OpenAsync(ct);
        await using var extension = new NpgsqlCommand("SELECT EXISTS(SELECT 1 FROM pg_extension WHERE extname = 'vector')", connection);
        if (await extension.ExecuteScalarAsync(ct) is not true)
            return (await ReadAsync(identity, keys, ct)).Where(v => v.Value.Length == query.Length)
                .ToDictionary(v => v.Key, v => VectorSimilarity.Cosine(query, v.Value));
        var scores = new Dictionary<string, double>();
        foreach (var batch in keys.Chunk(256))
        {
            // Exact search with membership filtering before ranking. No approximate-index omissions.
            await using var command = new NpgsqlCommand("""
                SELECT key, 1 - (embedding::vector <=> $3::real[]::vector)
                FROM chat.embedding_vectors WHERE identity = $1 AND key = ANY($2) AND cardinality(embedding) = $4
                """, connection);
            command.Parameters.AddWithValue(identity); command.Parameters.AddWithValue(batch);
            command.Parameters.AddWithValue(query); command.Parameters.AddWithValue(query.Length);
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct)) if (!reader.IsDBNull(1)) scores[reader.GetString(0)] = reader.GetDouble(1);
        }
        return scores;
    }
}
