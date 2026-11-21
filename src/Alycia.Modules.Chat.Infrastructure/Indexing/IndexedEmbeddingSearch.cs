using System.Text;
using Alycia.Modules.Chat.Application;
using Alycia.Modules.Chat.Domain;

namespace Alycia.Modules.Chat.Infrastructure;

public sealed class IndexedEmbeddingSearch(IEmbeddingSimilarityReader store, ILocalEmbeddings embeddings) : IIndexedEmbeddings
{
    public async Task<SemanticSearch> SearchAsync(Guid conversation, string query, ChatOptions settings,
        IReadOnlyList<SourceChunk> chunks, CancellationToken ct)
    {
        try { return await SearchCoreAsync(query, EmbeddingSettings.ForQuery(settings), chunks, ct); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception e) when (e is ChatModelException or ArgumentException or HttpRequestException or OperationCanceledException)
        { return new(new double[chunks.Count], 0, [], "Recherche sémantique indisponible ; recherche lexicale utilisée. Vérifiez l’indexation et le modèle d’embeddings."); }
    }

    private async Task<SemanticSearch> SearchCoreAsync(string query, EmbeddingSettings profile,
        IReadOnlyList<SourceChunk> chunks, CancellationToken ct)
    {
        var identity = await embeddings.IdentityAsync(profile.Model, ct);
        var parts = chunks.Select(c => EmbeddingChunks.Split(c, profile.ChunkCharacters).ToArray()).ToArray();
        var keys = parts.SelectMany(p => p).Select(EmbeddingChunks.Key).Distinct().ToArray();
        var calls = new List<ModelCallUsage>();
        if (keys.Length == 0) return new(new double[chunks.Count], 0, calls);
        var queryVectors = await QueryVectorsAsync(query, profile, identity, calls, ct);
        var scores = new Dictionary<string, double>();
        foreach (var vector in queryVectors)
            foreach (var pair in await store.SimilaritiesAsync(profile.VectorSpace(identity, vector.Length), keys, vector, ct))
                scores[pair.Key] = Math.Max(scores.GetValueOrDefault(pair.Key, -1), pair.Value);
        return new(parts.Select(p => p.Select(c => scores.GetValueOrDefault(EmbeddingChunks.Key(c), 0)).DefaultIfEmpty(0).Max()).ToArray(),
            parts.Count(p => p.Length > 0 && p.All(c => scores.ContainsKey(EmbeddingChunks.Key(c)))), calls);
    }

    private async Task<List<float[]>> QueryVectorsAsync(string query, EmbeddingSettings profile,
        string identity, List<ModelCallUsage> calls, CancellationToken ct)
    {
        // Query vectors only. Corpus vectors are produced by the durable worker, never by a chat request.
        var queryChunk = new SourceChunk(Guid.Empty, "", "", "", "query", 0, query.Length, 1, 1, query, [], true);
        var queryVectors = new List<float[]>();
        foreach (var batch in EmbeddingChunks.Split(queryChunk, profile.ChunkCharacters).Chunk(profile.BatchSize))
        {
            var timer = System.Diagnostics.Stopwatch.StartNew();
            var result = await embeddings.EmbedAsync(profile.Model, batch.Select(c => profile.QueryPrefix + c.Text).ToArray(), ct);
            calls.Add(new("embedding-query:" + identity, batch.Sum(c => Encoding.UTF8.GetByteCount(c.Text)), timer.Elapsed.TotalMilliseconds, result.Stats));
            queryVectors.AddRange(result.Vectors);
        }
        if (profile.Versioned && await embeddings.IdentityAsync(profile.Model, ct) != identity)
            throw new ChatModelException("Modèle modifié pendant la recherche.");
        return queryVectors;
    }

    public async Task<MemoryVectorMatches> SearchMemoryAsync(Guid owner, string question, ChatOptions settings, int through, DateTimeOffset before, CancellationToken ct)
    {
        var calls = new List<ModelCallUsage>(); var scores = new Dictionary<int, double>();
        try
        {
            var profile = EmbeddingSettings.ForQuery(settings);
            var identity = await embeddings.IdentityAsync(profile.Model, ct);
            foreach (var vector in await QueryVectorsAsync(question, profile, identity, calls, ct))
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
