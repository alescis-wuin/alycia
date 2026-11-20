using System.Text;
using Alycia.Modules.Chat.Application;
using Alycia.Modules.Chat.Domain;
using Microsoft.EntityFrameworkCore;

namespace Alycia.Modules.Chat.Infrastructure;

public sealed class BackgroundEmbeddingIndex(ChatDbContext database, PostgresEmbeddingStore store,
    ProjectSourceReader reader, ILocalEmbeddings embeddings, ChatOptions defaults) : IIndexedEmbeddings
{
    public async Task DiscoverAsync(CancellationToken ct)
    {
        var conversations = await database.Conversations.AsNoTracking().ToArrayAsync(ct);
        var projects = await database.WorkspaceProfiles.AsNoTracking().Where(p => !p.Archived).ToDictionaryAsync(p => p.Id, ct);
        foreach (var conversation in conversations)
        {
            var options = ConversationConfiguration.Options(conversation, defaults);
            if (options.RetrievalMode == "hybrid" && !string.IsNullOrWhiteSpace(options.EmbeddingModel))
                await store.QueueAsync(conversation.Id, false, options, ct, sourceRevision: conversation.Revision + ":"
                    + (conversation.WorkspaceId is Guid project && projects.TryGetValue(project, out var profile) ? profile.Revision : 0));
        }
        if (defaults.RetrievalMode == "hybrid" && !string.IsNullOrWhiteSpace(defaults.EmbeddingModel))
        {
            var existing = (await store.JobsAsync(ct)).Where(j => j.Progress.IsProject).Select(j => j.Progress.OwnerId).ToHashSet();
            foreach (var project in projects.Values.Where(p => !existing.Contains(p.Id)))
                await store.QueueAsync(project.Id, true, defaults, ct, sourceRevision: project.Revision.ToString());
        }
        foreach (var job in (await store.JobsAsync(ct)).Where(j => j.Progress.IsProject))
        {
            if (!projects.TryGetValue(job.Progress.OwnerId, out var project)) { await store.DeleteJobAsync(job, ct); continue; }
            var options = defaults.Copy(); options.EmbeddingModel = job.Settings.Model; options.EmbeddingBatchSize = job.Settings.BatchSize;
            options.EmbeddingChunkCharacters = job.Settings.ChunkCharacters; options.EmbedHistory = job.Settings.History;
            options.HierarchicalIndexEnabled = job.Settings.Hierarchical;
            options.EmbeddingDocumentPrefix = job.Settings.DocumentPrefix; options.EmbeddingQueryPrefix = job.Settings.QueryPrefix;
            await store.QueueAsync(project.Id, true, options, ct, sourceRevision: project.Revision.ToString());
        }
    }

    public async Task ProcessAsync(EmbeddingJob job, CancellationToken ct)
    {
        var owner = job.Progress.OwnerId;
        var project = job.Progress.IsProject;
        var conversation = project ? null : await database.Conversations.AsNoTracking().SingleOrDefaultAsync(c => c.Id == owner, ct);
        if (project ? !await database.WorkspaceProfiles.AnyAsync(p => p.Id == owner, ct) : conversation is null)
        { await store.DeleteJobAsync(job, ct); return; }
        if (conversation is not null)
        {
            var options = ConversationConfiguration.Options(conversation, defaults);
            if (options.RetrievalMode != "hybrid" || string.IsNullOrWhiteSpace(options.EmbeddingModel))
            { await store.DeleteJobAsync(job, ct); return; }
        }
        if (job.Progress.State is "failed" or "paused" || job.Progress.State == "completed"
            && DateTimeOffset.UtcNow - job.Progress.UpdatedAt < TimeSpan.FromSeconds(30)) return;
        await using var lease = await store.TryLockAsync(job, ct);
        if (lease is null) return;
        var completed = 0; var total = 0; var fingerprint = job.Fingerprint;
        try
        {
            var sources = project ? database.Attachments.AsNoTracking().Where(f => f.WorkspaceId == owner)
                : database.ModelSources(owner);
            var files = await sources.Where(f => f.ContextMode != "excluded" && f.Text != "").OrderBy(f => f.Id).ToArrayAsync(ct);
            var messages = !project && job.Settings.History ? conversation!.MessageCount : 0;
            total = files.Length + messages;
            var digest = await embeddings.IdentityAsync(job.Settings.Model, ct);
            // Probe only once per processing pass when the digest or job inputs require indexing.
            var identity = digest;
            fingerprint = StructuredMemory.Hash($"{job.Settings.VectorSpace(digest, 0)}|{CodeStructure.Version}|{StructuredDocuments.Version}|{messages}|"
                + string.Join('|', files.Select(f => $"{f.Id}:{f.Sha256}:{f.Name}:{f.ContextMode}")));
            if (job.Progress.State == "completed" && fingerprint == job.Fingerprint)
            { await store.ProgressAsync(job, "completed", total, total, job.Progress.Detail, fingerprint, ct); return; }
            if (!await store.ProgressAsync(job, "running", 0, total, "Préparation des passages", fingerprint, ct)) return;
            if (GenerationGate.Shared.ForegroundRequested) return;
            var dimensions = 0;
            if (total > 0 && job.Settings.Versioned)
            {
                var probe = await embeddings.EmbedAsync(job.Settings.Model, [job.Settings.DocumentPrefix + "dimension"], ct);
                dimensions = probe.Vectors.Single().Length;
                identity = job.Settings.VectorSpace(digest, dimensions);
            }
            var omissions = 0;
            foreach (var file in files)
            {
                ct.ThrowIfCancellationRequested();
                var source = await reader.ReadAsync(file, ct);
                if (!source.Complete) omissions++;
                var chunks = CodeStructure.Split(file.Id, file.Name, file.Sha256, source.Text, ct, job.Settings.Hierarchical)
                    .SelectMany(c => EmbeddingChunks.Split(c with { CompleteSource = source.Complete }, job.Settings.ChunkCharacters));
                if (!await IndexAsync(chunks, null, 0, file.Name)) return;
                completed++;
            }
            // Page immutable history; never load a conversation's complete history into memory.
            for (var after = 0; after < messages;)
            {
                var page = await database.Messages.AsNoTracking().Where(m => m.ConversationId == owner && m.Sequence > after && m.Sequence <= messages)
                    .OrderBy(m => m.Sequence).Take(32).ToArrayAsync(ct);
                if (page.Length == 0) break;
                foreach (var message in page)
                {
                    var chunk = new SourceChunk(message.RequestId, "history/" + message.Sequence, StructuredMemory.Hash(message.Content),
                        message.Role, "history", 0, message.Content.Length, 1, 1 + message.Content.Count(c => c == '\n'), message.Content, [], true);
                    // Conversation ID is included even for imported/forked messages sharing a request ID.
                    chunk = chunk with { Path = owner.ToString("N") + "/" + chunk.Path };
                    if (!await IndexAsync(EmbeddingChunks.Split(chunk, job.Settings.ChunkCharacters), owner, message.Sequence, "Message " + message.Sequence)) return;
                    completed++; after = message.Sequence;
                }
            }
            await store.ProgressAsync(job, "completed", completed, total, omissions == 0 ? "Indexation terminée"
                : $"Indexation terminée · {omissions} extraction(s) documentaire(s) partielle(s)", fingerprint, ct);

            async Task<bool> IndexAsync(IEnumerable<SourceChunk> chunks, Guid? historyOwner, int sequence, string detail)
            {
                var passages = 0;
                foreach (var batch in chunks.DistinctBy(EmbeddingChunks.Key).Chunk(job.Settings.BatchSize))
                {
                    if (GenerationGate.Shared.ForegroundRequested)
                    {
                        await store.ProgressAsync(job, "queued", completed, total, "Indexation différée pendant une génération", fingerprint, ct);
                        return false;
                    }
                    if (!await store.ProgressAsync(job, "running", completed, total, $"{detail} · {passages} passages traités", fingerprint, ct)) return false;
                    var cached = await store.ReadAsync(identity, batch.Select(EmbeddingChunks.Key).ToArray(), ct);
                    var missing = batch.Where(c => !cached.ContainsKey(EmbeddingChunks.Key(c))).ToArray();
                    if (missing.Length > 0)
                    {
                        var result = await embeddings.EmbedAsync(job.Settings.Model, missing.Select(c => job.Settings.DocumentPrefix + EmbeddingChunks.Text(c)).ToArray(), ct);
                        if (result.Vectors.Length != missing.Length) throw new ChatModelException("Nombre de vecteurs inattendu.");
                        if (job.Settings.Versioned && (result.Vectors.Any(v => v.Length != dimensions)
                            || await embeddings.IdentityAsync(job.Settings.Model, ct) != digest))
                            throw new ChatModelException("Identité ou dimension du modèle modifiée pendant l’indexation.");
                        for (var i = 0; i < missing.Length; i++) await store.WriteAsync(identity, missing[i], result.Vectors[i], historyOwner, sequence, ct);
                    }
                    passages += batch.Length;
                }
                return true;
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception e) when (e is ArgumentException or ChatModelException or HttpRequestException or IOException or OperationCanceledException)
        {
            // Provider payloads may contain source text: expose only our own controlled error messages.
            var detail = e is ArgumentException or ChatModelException ? e.Message : "Fournisseur ou source indisponible. Les lots terminés sont conservés ; réessayez.";
            await store.ProgressAsync(job, "failed", completed, total, detail, fingerprint, ct);
        }
    }

    public async Task<SemanticSearch> SearchAsync(Guid conversation, string query, ChatOptions settings,
        IReadOnlyList<SourceChunk> chunks, CancellationToken ct)
    {
        try { return await SearchCoreAsync(query, settings, chunks, ct); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception e) when (e is ChatModelException or ArgumentException or HttpRequestException or OperationCanceledException)
        { return new(new double[chunks.Count], 0, [], "Recherche sémantique indisponible ; recherche lexicale utilisée. Vérifiez l’indexation et le modèle d’embeddings."); }
    }

    private async Task<SemanticSearch> SearchCoreAsync(string query, ChatOptions settings,
        IReadOnlyList<SourceChunk> chunks, CancellationToken ct)
    {
        var identity = await embeddings.IdentityAsync(settings.EmbeddingModel, ct);
        var profile = EmbeddingSettings.From(settings);
        var parts = chunks.Select(c => EmbeddingChunks.Split(c, settings.EmbeddingChunkCharacters).ToArray()).ToArray();
        var keys = parts.SelectMany(p => p).Select(EmbeddingChunks.Key).Distinct().ToArray();
        var calls = new List<ModelCallUsage>();
        if (keys.Length == 0) return new(new double[chunks.Count], 0, calls);
        var queryVectors = await QueryVectorsAsync(query, settings, profile, identity, calls, ct);
        var scores = new Dictionary<string, double>();
        foreach (var vector in queryVectors)
            foreach (var pair in await store.SimilaritiesAsync(profile.VectorSpace(identity, vector.Length), keys, vector, ct))
                scores[pair.Key] = Math.Max(scores.GetValueOrDefault(pair.Key, -1), pair.Value);
        return new(parts.Select(p => p.Select(c => scores.GetValueOrDefault(EmbeddingChunks.Key(c), 0)).DefaultIfEmpty(0).Max()).ToArray(),
            parts.Count(p => p.Length > 0 && p.All(c => scores.ContainsKey(EmbeddingChunks.Key(c)))), calls);
    }

    private async Task<List<float[]>> QueryVectorsAsync(string query, ChatOptions settings, EmbeddingSettings profile,
        string identity, List<ModelCallUsage> calls, CancellationToken ct)
    {
        // Query vectors only. Corpus vectors are produced by the durable worker, never by a chat request.
        var queryChunk = new SourceChunk(Guid.Empty, "", "", "", "query", 0, query.Length, 1, 1, query, [], true);
        var queryVectors = new List<float[]>();
        foreach (var batch in EmbeddingChunks.Split(queryChunk, settings.EmbeddingChunkCharacters).Chunk(settings.EmbeddingBatchSize))
        {
            var timer = System.Diagnostics.Stopwatch.StartNew();
            var result = await embeddings.EmbedAsync(settings.EmbeddingModel, batch.Select(c => profile.QueryPrefix + c.Text).ToArray(), ct);
            calls.Add(new("embedding-query:" + identity, batch.Sum(c => Encoding.UTF8.GetByteCount(c.Text)), timer.Elapsed.TotalMilliseconds, result.Stats));
            queryVectors.AddRange(result.Vectors);
        }
        if (profile.Versioned && await embeddings.IdentityAsync(settings.EmbeddingModel, ct) != identity)
            throw new ChatModelException("Modèle modifié pendant la recherche.");
        return queryVectors;
    }

    public async Task<MemoryVectorMatches> SearchMemoryAsync(Guid owner, string question, ChatOptions settings, int through, DateTimeOffset before, CancellationToken ct)
    {
        var calls = new List<ModelCallUsage>(); var scores = new Dictionary<int, double>();
        try
        {
            var profile = EmbeddingSettings.From(settings);
            var identity = await embeddings.IdentityAsync(settings.EmbeddingModel, ct);
            foreach (var vector in await QueryVectorsAsync(question, settings, profile, identity, calls, ct))
                foreach (var hit in await store.MemorySimilaritiesAsync(owner, profile.VectorSpace(identity, vector.Length), through, before, vector, ct))
                    scores[hit.Key] = Math.Max(scores.GetValueOrDefault(hit.Key, -1), hit.Value);
            return new(scores, calls);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex) when (ex is ChatModelException or ArgumentException or HttpRequestException or OperationCanceledException)
        { return new(scores, calls, "Vecteurs historiques indisponibles ; rappel lexical utilisé."); }
    }

    public Task<SemanticSearch> SearchHistoryAsync(Guid conversation, string query, ChatOptions settings,
        IReadOnlyList<ChatMessage> messages, CancellationToken ct)
    {
        if (messages.Any(m => m.ConversationId != conversation)) throw new ArgumentException("Historique hors de la conversation.");
        var chunks = messages.Select(m => new SourceChunk(m.RequestId, conversation.ToString("N") + "/history/" + m.Sequence,
            StructuredMemory.Hash(m.Content), m.Role, "history", 0, m.Content.Length, 1, 1 + m.Content.Count(c => c == '\n'), m.Content, [], true)).ToArray();
        return SearchAsync(conversation, query, settings, chunks, ct);
    }
}
