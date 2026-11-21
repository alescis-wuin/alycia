using Alycia.Contracts.Indexing;
using Alycia.Contracts.Context;
using Alycia.Contracts.Workspaces;
using Alycia.Modules.Chat.Application;
using Alycia.Modules.Chat.Domain;
using Microsoft.EntityFrameworkCore;

namespace Alycia.Modules.Chat.Infrastructure;

public sealed class BackgroundEmbeddingIndex(IIndexSourceDocuments documents, ILiveIndexFileAccess live, PostgresEmbeddingStore store,
     ILocalEmbeddings embeddings, ChatOptions defaults, IConversationIndexOwners conversations, IProjectIndexOwners projects, IWorkspaceProfiles profiles, IForegroundActivity foreground, KnowledgeBuilder? knowledge = null) : IIndexedEmbeddings
{
    private readonly EmbeddingJobDiscovery _discovery = new(conversations, projects, store, defaults, knowledge);
    private readonly IndexedEmbeddingSearch _queries = new(store, embeddings);

    public Task DiscoverAsync(CancellationToken ct) => _discovery.RunAsync(ct);

    public async Task ProcessAsync(EmbeddingJob job, CancellationToken ct)
    {
        var owner = job.Progress.OwnerId;
        var project = job.Progress.IsProject;
        var conversation = project ? null : await conversations.FindAsync(owner, ct);
        if (project ? await profiles.FindAsync(owner, ct) is null : conversation is null)
        { await store.DeleteJobAsync(job, ct); return; }
        if (conversation is not null)
        {
            var options = ConversationConfiguration.Options(conversation.SettingsJson, defaults);
            if (!EmbeddingSettings.RequiresJob(options))
            { await store.DeleteJobAsync(job, ct); return; }
        }
        if (!job.IsDue(DateTimeOffset.UtcNow)) return;
        await using var lease = await store.TryLockAsync(job, ct);
        if (lease is null) return;
        var completed = 0; var total = 0; var fingerprint = job.Fingerprint;
        var budget = job.Settings.Enrichment is { Enabled: true } limits ? new ModelCallBudget(limits.JobCalls) : null;
        void Reserve() { if (budget?.TryReserve() == false) throw new IndexSliceEnded("Budget de ce cycle atteint. Reprise automatique."); }
        try
        {
            var files = await IndexFiles.SnapshotAsync(documents, owner, project, null, false, ct);
            var messages = !project && job.Settings.History ? conversation!.MessageCount : 0;
            total = files.Length + messages;
            var vectorEnabled = !string.IsNullOrWhiteSpace(job.Settings.Model);
            var pass = job.Settings.Enrichment is { Enabled: true } config && knowledge is not null
                ? await knowledge.StartAsync(config, async (fileId, token) => await store.ActiveAsync(job, token)
                    && (!fileId.HasValue || await live.IsAllowedAsync(owner, project, fileId.Value, token)), ct, job.RetryAt, budget) : null;
            var digest = ""; string? vectorWarning = null;
            if (vectorEnabled)
            {
                try { digest = await embeddings.IdentityAsync(job.Settings.Model, ct); }
                catch (Exception ex) when (pass is not null && (ex is ChatModelException or HttpRequestException or ArgumentException))
                {
                    vectorEnabled = false;
                    vectorWarning = "Synthèses disponibles · embeddings indisponibles. Vérifiez le modèle.";
                }
            }

            // Probe only once per processing pass when the digest or job inputs require indexing.
            var identity = digest;
            fingerprint = MemoryTransfer.Hash($"{pass?.Identity}|{job.Settings.Enrichment?.Profile}|{job.Settings.VectorSpace(digest, 0)}|{IndexedOriginal.StructureVersion}|{StructuredDocuments.Version}|{messages}|"
                + string.Join('|', files.Select(f => $"{f.Id}:{f.Sha256}:{f.Name}:{f.Status}:{f.SourceCreatedAt}:{f.SourceModifiedAt}:{f.ContextMode}")));
            if (job.Progress.State == "completed" && fingerprint == job.Fingerprint)
            { await store.ProgressAsync(job, "completed", total, total, job.Progress.Detail, fingerprint, ct); return; }
            if (!await store.ProgressAsync(job, "running", 0, total, "Préparation des passages", fingerprint, ct, 0)) return;
            if (foreground.ForegroundRequested)
            {
                await store.ProgressAsync(job, "queued", completed, total, "Indexation différée pendant une génération", fingerprint, ct, 0);
                return;
            }
            var dimensions = 0;
            if (vectorEnabled && total > 0 && job.Settings.Versioned)
            {
                if (job.VectorDigest == digest && job.VectorDimensions is > 0) dimensions = job.VectorDimensions.Value;
                else
                {
                    Reserve();
                    var probe = await embeddings.EmbedAsync(job.Settings.Model, [job.Settings.DocumentPrefix + "dimension"], ct);
                    dimensions = probe.Vectors.Single().Length;
                    await store.SetDimensionsAsync(job, digest, dimensions, ct);
                }
                identity = job.Settings.VectorSpace(digest, dimensions);
            }
            var omissions = 0;
            if (pass is not null)
            {
                var readyFiles = 0;
                string? deferred = null;
                await Parallel.ForEachAsync(files, new ParallelOptions { MaxDegreeOfParallelism = job.Settings.Enrichment!.Workers, CancellationToken = ct },
                    async (file, token) =>
                    {
                        if (Volatile.Read(ref deferred) is not null) return;
                        try
                        {
                            await store.ProgressAsync(job, "running", Volatile.Read(ref readyFiles), total, "Synthèse · " + file.Name, fingerprint, token, budget?.Used);
                            var root = await pass.FileAsync(owner, project, file, documents, job.Settings.Hierarchical, token);
                            if (!root.Complete || root.HasFallback) Interlocked.Increment(ref omissions);
                            Interlocked.Increment(ref readyFiles);
                        }
                        catch (IndexSliceEnded e) { Interlocked.CompareExchange(ref deferred, e.Message, null); }
                    });
                if (!vectorEnabled) completed = readyFiles;
                if (deferred is not null) throw new IndexSliceEnded(deferred);
            }
            foreach (var file in vectorEnabled ? files : [])
            {
                ct.ThrowIfCancellationRequested();
                var source = await documents.ReadAsync(owner, project, file.Id, file.Sha256, ct);
                if (!source.Complete) omissions++;
                var chunks = documents.Split(file, source, job.Settings.Hierarchical, ct)
                    .SelectMany(c => EmbeddingChunks.Split(c with { CompleteSource = source.Complete }, job.Settings.ChunkCharacters));
                if (!await IndexAsync(chunks, null, 0, file.Name)) return;
                completed++;
            }
            // Page immutable history; never load a conversation's complete history into memory.
            for (var after = 0; after < messages;)
            {
                var page = await conversations.HistoryPageAsync(owner, after, messages, 32, ct);
                if (page.Length == 0) break;
                if (pass is not null) await pass.HistoryAsync(owner, page, ct);
                foreach (var message in page)
                {
                    var chunk = new SourceChunk(message.RequestId, "history/" + message.Sequence, MemoryTransfer.Hash(message.Content),
                        message.Role, "history", 0, message.Content.Length, 1, 1 + message.Content.Count(c => c == '\n'), message.Content, [], true);
                    // Conversation ID is included even for imported/forked messages sharing a request ID.
                    chunk = chunk with { Path = owner.ToString("N") + "/" + chunk.Path };
                    if (vectorEnabled && !await IndexAsync(EmbeddingChunks.Split(chunk, job.Settings.ChunkCharacters), owner, message.Sequence, "Message " + message.Sequence)) return;
                    completed++; after = message.Sequence;
                }
            }
            await store.ProgressAsync(job, "completed", completed, total, vectorWarning ?? (omissions == 0 ? "Indexation terminée"
                : $"Indexation terminée · {omissions} signalement(s) de source partielle ou de repli"), fingerprint, ct, budget?.Used);

            async Task<bool> IndexAsync(IEnumerable<SourceChunk> chunks, Guid? historyOwner, int sequence, string detail)
            {
                var passages = 0;
                foreach (var batch in chunks.DistinctBy(EmbeddingChunks.Key).Chunk(job.Settings.BatchSize))
                {
                    if (foreground.ForegroundRequested)
                    {
                        await store.ProgressAsync(job, "queued", completed, total, "Indexation différée pendant une génération", fingerprint, ct);
                        return false;
                    }
                    if (!await store.ProgressAsync(job, "running", completed, total, $"{detail} · {passages} passages traités", fingerprint, ct, budget?.Used)) return false;
                    var cached = await store.ReadAsync(identity, batch.Select(EmbeddingChunks.Key).ToArray(), ct);
                    var missing = batch.Where(c => !cached.ContainsKey(EmbeddingChunks.Key(c))).ToArray();
                    if (missing.Length > 0)
                    {
                        async Task<bool> CanPublishAsync() => await store.ActiveAsync(job, ct)
                            && (historyOwner.HasValue || await live.IsAllowedAsync(owner, project, missing[0].FileId, ct));
                        if (!await CanPublishAsync()) return false;
                        Reserve();
                        var result = await embeddings.EmbedAsync(job.Settings.Model, missing.Select(c => job.Settings.DocumentPrefix + EmbeddingChunks.Text(c)).ToArray(), ct);
                        if (result.Vectors.Length != missing.Length) throw new ChatModelException("Nombre de vecteurs inattendu.");
                        if (job.Settings.Versioned && (result.Vectors.Any(v => v.Length != dimensions)
                            || await embeddings.IdentityAsync(job.Settings.Model, ct) != digest))
                            throw new ChatModelException("Identité ou dimension du modèle modifiée pendant l’indexation.");
                        if (!await CanPublishAsync()) return false;
                        for (var i = 0; i < missing.Length; i++) await store.WriteAsync(identity, missing[i], result.Vectors[i], historyOwner, sequence, ct);
                    }
                    passages += batch.Length;
                }
                return true;
            }
        }
        catch (IndexSliceEnded e)
        {
            await store.ProgressAsync(job, "partial", completed, total, e.Message, fingerprint, ct, budget?.Used);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception e) when (e is ArgumentException or ChatModelException or HttpRequestException or IOException or OperationCanceledException)
        {
            // Provider payloads may contain source text: expose only our own controlled error messages.
            var detail = e is ArgumentException or ChatModelException ? e.Message : "Fournisseur ou source indisponible. Les lots terminés sont conservés ; réessayez.";
            await store.ProgressAsync(job, "failed", completed, total, detail, fingerprint, ct);
        }
    }

    public Task<SemanticSearch> SearchAsync(Guid conversation, string query, EmbeddingQuery settings,
        IReadOnlyList<SourceChunk> chunks, CancellationToken ct) =>
        _queries.SearchAsync(conversation, query, settings, chunks, ct);

    public Task<MemoryVectorMatches> SearchMemoryAsync(Guid owner, string question, EmbeddingQuery settings,
        int through, DateTimeOffset before, CancellationToken ct) =>
        _queries.SearchMemoryAsync(owner, question, settings, through, before, ct);

    public Task<SemanticSearch> SearchHistoryAsync(Guid conversation, string query, EmbeddingQuery settings,
        IReadOnlyList<HistoryMessage> messages, CancellationToken ct) =>
        _queries.SearchHistoryAsync(conversation, query, settings, messages, ct);
}
