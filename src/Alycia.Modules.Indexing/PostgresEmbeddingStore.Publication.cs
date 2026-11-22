using System.Data;
using Npgsql;

namespace Alycia.Modules.Indexing;

public sealed partial class PostgresEmbeddingStore
{
    public async Task<bool> PublishEligibleVectorsAsync(EmbeddingJob job, string digest, IReadOnlyList<SourceChunk> passages,
        IReadOnlyList<float[]> vectors, Guid? conversation, int sequence, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(digest) || passages.Count == 0 || passages.Count != vectors.Count
            || passages.Count > job.Settings.BatchSize || string.IsNullOrWhiteSpace(job.Settings.Model))
            throw new ChatModelException("Lot de vecteurs invalide.");
        if (conversation.HasValue ? conversation != job.Progress.OwnerId || job.Progress.IsProject || sequence < 1 || !job.Settings.History : sequence != 0)
            return false;

        var batch = passages.Select((chunk, i) => (Chunk: chunk, Vector: vectors[i].ToArray(), Key: EmbeddingChunks.Key(chunk)))
            .OrderBy(entry => entry.Key, StringComparer.Ordinal).ToArray();
        var dimensions = batch[0].Vector.Length;
        if (dimensions is < 1 or > 8192 || batch.Any(entry => entry.Vector.Length != dimensions || entry.Vector.Any(n => !float.IsFinite(n))
            || entry.Chunk.FileId == Guid.Empty || string.IsNullOrWhiteSpace(entry.Chunk.Sha256)
            || entry.Chunk.Start < 0 || entry.Chunk.End < entry.Chunk.Start || entry.Chunk.End - entry.Chunk.Start != entry.Chunk.Text.Length))
            throw new ChatModelException("Vecteur ou coordonnées de source invalides.");
        var identity = job.Settings.VectorSpace(digest, dimensions);
        var hashes = batch.Select(item => (item.Chunk.FileId, item.Chunk.Sha256)).ToHashSet();

        // If COMMIT lost its reply, replay this exact batch once. Its primary key is
        // deterministic and the second attempt must acquire and validate every grant again.
        for (var attempt = 0; ; attempt++)
        {
            await using var connection = await OpenAsync(ct);
            await using var tx = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
            try
            {
                await using var limits = new NpgsqlCommand("SET LOCAL lock_timeout = '3s'", connection, tx);
                await limits.ExecuteNonQueryAsync(ct);
                await using var active = new NpgsqlCommand("""
                    SELECT options_json,vector_digest,vector_dimensions FROM chat.embedding_jobs
                    WHERE owner_id=$1 AND is_project=$2 AND revision=$3 AND state IN ('queued','partial','running')
                    FOR UPDATE
                    """, connection, tx);
                active.Parameters.AddWithValue(job.Progress.OwnerId);
                active.Parameters.AddWithValue(job.Progress.IsProject);
                active.Parameters.AddWithValue(job.Revision);
                await using (var row = await active.ExecuteReaderAsync(ct))
                {
                    if (!await row.ReadAsync(ct) || System.Text.Json.JsonSerializer.Deserialize<EmbeddingSettings>(
                        row.GetString(0), ProjectToolCatalog.Json) != job.Settings) return false;
                    if (row.IsDBNull(1) != row.IsDBNull(2) || job.Settings.Versioned && row.IsDBNull(1)
                        || !row.IsDBNull(1) && (row.GetString(1) != digest || row.GetInt32(2) != dimensions))
                        return false;
                }
                var scope = sourceFor(tx, true);
                var revision = await scope.RevisionAsync(job.Progress.OwnerId, job.Progress.IsProject, ct);
                if (revision is null || job.Settings.SourceRevision != revision)
                    return false;
                if (conversation is Guid owner)
                {
                    foreach (var entry in batch)
                        if (!await scope.HistoryMatchesAsync(owner, sequence, entry.Chunk, ct)) return false;
                }
                else if ((await scope.EligibleFilesAsync(job.Progress.OwnerId, job.Progress.IsProject, hashes, ct)).Count != hashes.Count)
                    return false;
                if (!job.Settings.Versioned)
                {
                    await using var bind = new NpgsqlCommand("""
                        UPDATE chat.embedding_jobs SET vector_digest=$1,vector_dimensions=$2
                        WHERE owner_id=$3 AND is_project=$4 AND revision=$5 AND vector_digest IS NULL
                        """, connection, tx);
                    bind.Parameters.AddWithValue(digest);
                    bind.Parameters.AddWithValue(dimensions);
                    bind.Parameters.AddWithValue(job.Progress.OwnerId);
                    bind.Parameters.AddWithValue(job.Progress.IsProject);
                    bind.Parameters.AddWithValue(job.Revision);
                    await bind.ExecuteNonQueryAsync(ct);
                }
                if (attempt > 0)
                {
                    var committed = await ReadAsync(connection, identity, batch.Select(e => e.Key).ToArray(), ct);
                    if (batch.All(e => committed.ContainsKey(e.Key))) return true;
                }
                foreach (var entry in batch)
                    await WriteAsync(tx, identity, entry.Chunk, entry.Vector, conversation, sequence, ct);
                try { await tx.CommitAsync(ct); }
                catch (NpgsqlException e) when (e is not PostgresException && attempt == 0 && !ct.IsCancellationRequested)
                { continue; }
                return true;
            }
            catch (PostgresException ex) when (ex.SqlState is PostgresErrorCodes.LockNotAvailable or PostgresErrorCodes.DeadlockDetected)
            {
                // Release all locks. Discovery retries the running job using current grants.
                return false;
            }
        }
    }
}
