using Alycia.Modules.Chat.Domain;
using Alycia.Modules.Testing;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Alycia.Modules.Chat.Tests;

public sealed class VectorPublicationConcurrencyTests
{
    [PostgresFact]
    public Task Every_file_is_checked_and_revoked_or_foreign_vectors_are_unreadable() =>
        WithSources(async (db, store, job, chunks, outsider) =>
        {
            var keys = chunks.Select(EmbeddingChunks.Key).ToArray();
            var id = chunks[1].FileId;
            await db.Attachments.Where(f => f.Id == id).ExecuteUpdateAsync(u => u.SetProperty(f => f.ContextMode, "excluded"));
            Assert.False(await Publish(store, job, chunks));
            Assert.Equal(0, await CountAsync(db));
            await db.Attachments.Where(f => f.Id == id).ExecuteUpdateAsync(u => u.SetProperty(f => f.ContextMode, "window"));
            var concurrent = await Task.WhenAll(Publish(store, job, chunks), Publish(store, job, chunks));
            Assert.All(concurrent, Assert.True);
            Assert.Equal(2, await CountAsync(db));
            Assert.Empty(await store.SimilaritiesAsync(outsider, "digest", keys, [1, 0], default));
            await db.Attachments.Where(f => f.Id == id).ExecuteUpdateAsync(u => u.SetProperty(f => f.ContextMode, "excluded"));
            var visible = await store.SimilaritiesAsync(job.Progress.OwnerId, "digest", keys, [1, 0], default);
            Assert.Equal(keys[0], Assert.Single(visible).Key);
            Assert.False(await Publish(store, job, chunks));
            await store.PauseAsync(job.Progress.OwnerId, false, default);
            Assert.False(await Publish(store, job, [chunks[0]]));
            await store.QueueAsync(job.Progress.OwnerId, false, job.Settings, default, retry: true);
            Assert.False(await Publish(store, job, [chunks[0]]));
            var resumed = Assert.Single(await store.JobsAsync(default));
            Assert.True(await Publish(store, resumed, [chunks[0]]));
            using var cancel = new CancellationTokenSource();
            cancel.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.PublishEligibleVectorsAsync(
                resumed, "digest", [chunks[0]], [[1, 0]], null, 0, cancel.Token));
        });

    [PostgresFact]
    public Task Revocation_cannot_commit_between_the_eligibility_check_and_vector_commit() =>
        WithSources(async (db, store, job, chunks, _) =>
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            var ct = deadline.Token;
            await db.Database.ExecuteSqlRawAsync("""
                CREATE FUNCTION hold_publish() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN PERFORM pg_advisory_xact_lock(947262); RETURN NEW; END $$;
                CREATE TRIGGER hold_publish BEFORE INSERT ON chat.embedding_vectors FOR EACH ROW EXECUTE FUNCTION hold_publish()
                """, ct);
            await using var barrier = new NpgsqlConnection(db.Database.GetConnectionString());
            await using var revoke = new NpgsqlConnection(db.Database.GetConnectionString());
            await barrier.OpenAsync(ct);
            await revoke.OpenAsync(ct);
            await using var control = new NpgsqlCommand("SELECT pg_advisory_lock(947262)", barrier);
            await control.ExecuteNonQueryAsync(ct);
            var publishing = store.PublishEligibleVectorsAsync(job, "digest", chunks, [[1, 0], [1, 0]], null, 0, ct);
            var publisher = await WaitForBlockedAsync(db, barrier.ProcessID, ct);
            await using var removal = new NpgsqlCommand("""UPDATE chat.attachments SET "ContextMode"='excluded' WHERE "Id"=$1""", revoke);
            removal.Parameters.AddWithValue(chunks[1].FileId);
            var revoking = removal.ExecuteNonQueryAsync(ct);
            Assert.Equal(revoke.ProcessID, await WaitForBlockedAsync(db, publisher, ct));
            control.CommandText = "SELECT pg_advisory_unlock(947262)";
            await control.ExecuteNonQueryAsync(ct);
            Assert.True(await publishing);
            Assert.Equal(1, await revoking);
            Assert.False(await Publish(store, job, chunks));
            Assert.Empty(await store.ReadAsync(job.Progress.OwnerId, false, "digest", [EmbeddingChunks.Key(chunks[1])], ct));
        });

    [PostgresFact]
    public Task A_rejected_commit_rolls_back_the_whole_batch_and_can_be_retried() =>
        WithSources(async (db, store, job, chunks, _) =>
        {
            await db.Database.ExecuteSqlRawAsync("""
                CREATE FUNCTION reject_batch() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN RAISE EXCEPTION 'test rollback'; END $$;
                CREATE CONSTRAINT TRIGGER reject_batch AFTER INSERT ON chat.embedding_vectors
                DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION reject_batch()
                """);
            await Assert.ThrowsAsync<PostgresException>(() => Publish(store, job, chunks));
            Assert.Equal(0, await CountAsync(db));
            await db.Database.ExecuteSqlRawAsync("DROP TRIGGER reject_batch ON chat.embedding_vectors");
            Assert.True(await Publish(store, job, chunks));
            Assert.Equal(2, await CountAsync(db));
        });

    [PostgresFact]
    public Task A_lost_server_commit_reply_is_confirmed_without_a_second_insert() =>
        WithSources(async (db, _, job, chunks, outsider) =>
        {
            // A replayed INSERT would fire even a BEFORE INSERT trigger with ON CONFLICT.
            await db.Database.ExecuteSqlRawAsync("""
                CREATE SEQUENCE publish_attempts;
                CREATE FUNCTION count_attempts() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN PERFORM nextval('publish_attempts'); RETURN NEW; END $$;
                CREATE TRIGGER attempted BEFORE INSERT ON chat.embedding_vectors FOR EACH ROW EXECUTE FUNCTION count_attempts()
                """);
            await using var fault = new CommitReplyLoss(db.Database.GetConnectionString()!);
            var store = ChatFixture.Embeddings(db, fault.ConnectionString);
            Assert.True(await Publish(store, job, chunks));
            Assert.Equal(1, fault.Drops);
            Assert.Equal(2, await CountAsync(db));
            Assert.Equal(2L, await db.Database.SqlQueryRaw<long>("SELECT last_value AS \"Value\" FROM publish_attempts").SingleAsync());
            Assert.False(await store.PublishEligibleVectorsAsync(job, "other-digest", chunks, [[1, 0], [1, 0]], null, 0, default));
        });

    [PostgresFact]
    public Task Overrides_membership_and_workspace_archiving_revoke_even_previously_cached_file_keys() =>
        WithSources(async (db, store, original, chunks, _) =>
        {
            var owner = original.Progress.OwnerId;
            var project = new Alycia.Modules.Workspaces.Domain.WorkspaceProfile { Id = Guid.NewGuid() };
            db.WorkspaceProfiles.Add(project);
            var chat = await db.Conversations.SingleAsync(c => c.Id == owner);
            chat.AssignWorkspace(project.Id);
            var file = await db.Attachments.SingleAsync(f => f.Id == chunks[1].FileId);
            file.ConversationId = null;
            file.WorkspaceId = project.Id;
            await db.SaveChangesAsync();
            Assert.False(await Publish(store, original, chunks));
            await store.QueueAsync(owner, false, original.Settings with { SourceRevision = "" }, default);
            var current = Assert.Single(await store.JobsAsync(default));
            Assert.True(await Publish(store, current, chunks));
            var key = EmbeddingChunks.Key(chunks[1]);
            var local = new WorkspaceFileOverride { ConversationId = owner, FileId = file.Id, Mode = "excluded" };
            db.WorkspaceFileOverrides.Add(local);
            await db.SaveChangesAsync();
            Assert.Empty(await store.ReadAsync(owner, false, "digest", [key], default));
            Assert.False(await Publish(store, current, chunks));
            db.Remove(local);
            project.Archived = true;
            await db.SaveChangesAsync();
            Assert.Empty(await store.ReadAsync(owner, false, "digest", [key], default));
            Assert.False(await Publish(store, current, chunks));
            project.Archived = false;
            chat.AssignWorkspace(null);
            await db.SaveChangesAsync();
            Assert.Empty(await store.ReadAsync(owner, false, "digest", [key], default));
            Assert.False(await Publish(store, current, chunks));
            await store.DeleteJobAsync(current, default);
            Assert.False(await Publish(store, current, chunks));
        });

    [PostgresFact]
    public Task Similarity_adapters_check_owner_model_dimension_and_historical_cutoffs() =>
        WithSources(async (db, store, original, chunks, outsider) =>
        {
            var id = original.Progress.OwnerId;
            Assert.True(await store.PublishEligibleVectorsAsync(original, "digest", chunks, [[1, 0], [0, 1]], null, 0, default));
            var chat = await db.Conversations.SingleAsync(c => c.Id == id);
            var sourceDate = DateTimeOffset.UnixEpoch.AddDays(100);
            var request = Guid.NewGuid();
            const string content = "mémoire";
            var hash = MemoryTransfer.Hash(content);
            db.AddRange(ChatMessage.Import(id, 1, request, "user", content, sourceDate),
                ChatMessage.Import(id, 2, request, "assistant", "réponse", sourceDate));
            chat.CompleteTurn();
            await db.SaveChangesAsync();
            await store.QueueAsync(id, false, original.Settings with { History = true, SourceRevision = "" }, default);
            var job = Assert.Single(await store.JobsAsync(default));
            var history = new SourceChunk(request, $"{id:N}/history/1", hash, "user", "history", 0, content.Length, 1, 1, content, [], true);
            Assert.True(await store.PublishEligibleVectorsAsync(job, "digest", [history], [[1, 0]], id, 1, default));
            var facts = System.Text.Json.JsonSerializer.Serialize(new[] { new { sha256 = hash } });
            await new Alycia.Modules.Memory.TemporalFactStorage(db.Database).SaveAsync(id, 1, sourceDate, [], facts, content, default);
            var a = EmbeddingChunks.Key(chunks[0]);
            var b = EmbeddingChunks.Key(chunks[1]);
            var h = EmbeddingChunks.Key(history);
            var mock = new SnapshotSimilarities([new(id, "digest", a, [1, 0]), new(id, "digest", b, [0, 1]), new(id, "digest", h, [1, 0], 1, sourceDate)]);
            var recorded = new RecordedIndexedEmbeddings([new(id,chunks[0].FileId,chunks[0].Sha256,job.Settings.Model,1),
                new(id,chunks[1].FileId,chunks[1].Sha256,job.Settings.Model,0),new(id,request,hash,job.Settings.Model,1,1,sourceDate)]);
            var settings = new EmbeddingQuery(job.Settings.Model, 8, 128, false, "", "");
            foreach (var search in new IIndexedEmbeddings[] { new IndexedEmbeddingSearch(store, new DigestEncoder()), recorded })
            {
                var result = await search.SearchAsync(id, "mémoire", settings, chunks, default);
                Assert.Equal(2, result.Hits); Assert.Equal(new double[] { 1, 0 }, result.Scores);
                result.Scores[0] = -1;
                Assert.Equal(1, result.Scores[0]);
                Assert.Equal(0, (await search.SearchAsync(outsider, "mémoire", settings, chunks, default)).Hits);
                Assert.Empty((await search.SearchMemoryAsync(id, "mémoire", settings, 1, sourceDate, default)).Scores);
                Assert.Contains(1, (await search.SearchMemoryAsync(id, "mémoire", settings, 1, sourceDate.AddSeconds(1), default)).Scores.Keys);
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => search.SearchAsync(id, "", settings, [], new(true)));
            }
            foreach (var reader in new IEmbeddingSimilarityReader[] { store, mock })
            {
                var scores = await reader.SimilaritiesAsync(id, "digest", [b, a, a, "absent"], [1, 0], default);
                Assert.Equal(2, scores.Count);
                Assert.Equal(1, scores[a], 6);
                Assert.Equal(0, scores[b], 6);
                Assert.Empty(await reader.SimilaritiesAsync(id, "autre", [a], [1, 0], default));
                Assert.Empty(await reader.SimilaritiesAsync(id, "digest", [a], [1], default));
                Assert.Empty(await reader.SimilaritiesAsync(outsider, "digest", [a, h], [1, 0], default));
                Assert.Empty(await reader.MemorySimilaritiesAsync(id, "digest", 1, sourceDate, [1, 0], default));
                Assert.Empty(await reader.MemorySimilaritiesAsync(id, "digest", 0, sourceDate.AddDays(1), [1, 0], default));
                Assert.Equal(1, Assert.Single(await reader.MemorySimilaritiesAsync(id, "digest", 1, sourceDate.AddSeconds(1), [1, 0], default)).Key);
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => reader.SimilaritiesAsync(id, "digest", [a], [1, 0], new(true)));
            }
        });

    private sealed class DigestEncoder : ILocalEmbeddings
    {
        public Task<string> IdentityAsync(string model, CancellationToken ct) => Task.FromResult("digest");
        public Task<EmbeddingBatch> EmbedAsync(string model, IReadOnlyList<string> inputs, CancellationToken ct) =>
            Task.FromResult(new EmbeddingBatch(inputs.Select(_ => new float[] { 1, 0 }).ToArray(), null));
    }

    private static async Task<int> WaitForBlockedAsync(ChatDbContext db, int blocker, CancellationToken ct)
    {
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var waiting = await db.Database.SqlQuery<int>($"""
                SELECT pid AS "Value" FROM pg_stat_activity WHERE {blocker}=ANY(pg_blocking_pids(pid))
                """).FirstOrDefaultAsync(ct);
            if (waiting != 0) return waiting;
            await Task.Yield();
        }
    }

    private static Task<int> CountAsync(ChatDbContext db) =>
        db.Database.SqlQueryRaw<int>("SELECT count(*)::int AS \"Value\" FROM chat.embedding_vectors").SingleAsync();

    private static Task<bool> Publish(PostgresEmbeddingStore store, EmbeddingJob job, SourceChunk[] chunks) =>
        store.PublishEligibleVectorsAsync(job, "digest", chunks, chunks.Select(_ => new float[] { 1, 0 }).ToArray(), null, 0, default);

    private static Task WithSources(Func<ChatDbContext, PostgresEmbeddingStore, EmbeddingJob, SourceChunk[], Guid, Task> test) =>
        ProjectCreationTests.WithDatabaseAsync(async (options, _) =>
        {
            await using var db = new ChatDbContext(options);
            await db.Database.ExecuteSqlRawAsync("CREATE EXTENSION IF NOT EXISTS vector");
            var owner = Conversation.Create("Propriétaire", "local", "");
            var foreign = Conversation.Create("Autre", "local", "");
            db.AddRange(owner, foreign);
            var files = new[] { "alpha", "beta" }.Select(text => new Attachment
            {
                Id = Guid.NewGuid(),
                ConversationId = owner.Id,
                Name = text + ".txt",
                Sha256 = MemoryTransfer.Hash(text),
                Text = text,
                Status = "ready",
                BatchId = Guid.NewGuid(),
                CreatedAtUtc = DateTimeOffset.UtcNow
            }).ToArray();
            db.AddRange(files);
            await db.SaveChangesAsync();
            var store = ChatFixture.Embeddings(db);
            await store.QueueAsync(owner.Id, false, new EmbeddingSettings("local", 8, 256, false), default);
            var job = Assert.Single(await store.JobsAsync(default));
            var chunks = files.Select(f => new SourceChunk(f.Id, f.Name, f.Sha256, "", "text", 0,
                f.Text.Length, 1, 1, f.Text, [], true)).ToArray();
            await test(db, store, job, chunks, foreign.Id);
        });
}
