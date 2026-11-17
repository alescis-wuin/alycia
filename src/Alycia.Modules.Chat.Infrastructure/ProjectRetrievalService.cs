using System.Text;
using System.Text.Json;
using Alycia.Modules.Chat.Application;

namespace Alycia.Modules.Chat.Infrastructure;

public sealed class ProjectRetrievalService(IProjectIndex index, ILocalEmbeddings embeddings, FileStorage storage, LexicalIndexCache? lexicalCache = null) : IProjectRetrieval
{
    private sealed record VectorCache(int Version, string Identity, Dictionary<string, float[]> Vectors);
    private readonly LexicalIndexCache _lexical = lexicalCache ?? new();
    private static readonly SemaphoreSlim EmbeddingGate = new(1, 1);
    public async Task<RetrievalPreparation> PrepareAsync(Guid conversation, string query, ChatOptions settings, CancellationToken ct)
    {
        var timer = System.Diagnostics.Stopwatch.StartNew();
        var snapshot = await index.BuildAsync(conversation, ct);
        var indexMilliseconds = timer.Elapsed.TotalMilliseconds;
        var calls = new List<ModelCallUsage>();
        double[]? similarities = null;
        var vectorHits = 0; var vectorMisses = 0; var vectorWritten = false;
        if (settings.RetrievalMode == "hybrid" && snapshot.Chunks.Count > 0)
        {
            if (snapshot.Chunks.Count > 2048) throw new ArgumentException("Recherche hybride limitée à 2048 passages : ciblez un import plus petit ou choisissez BM25.");
            await EmbeddingGate.WaitAsync(ct);
            try
            {
                var identity = await embeddings.IdentityAsync(settings.EmbeddingModel, ct);
                var path = Path.Combine(storage.DirectoryFor(conversation), "vectors-v1.json");
                VectorCache? cache = null;
                if (File.Exists(path) && new FileInfo(path).Length <= 128 * 1024 * 1024)
                {
                    try { cache = JsonSerializer.Deserialize<VectorCache>(await File.ReadAllTextAsync(path, ct), ProjectToolCatalog.Json); }
                    catch (JsonException) { }
                }
                var valid = cache is { Version: 2, Vectors: not null } && cache.Identity == identity
                    && cache.Vectors.Count <= 2048 && cache.Vectors.Values.All(v => v is { Length: > 0 and <= 8192 } && v.All(float.IsFinite))
                    && cache.Vectors.Values.Select(v => v.Length).Distinct().Count() <= 1;
                var vectors = valid ? cache!.Vectors! : new Dictionary<string, float[]>();
                string Text(SourceChunk c) => c.Path + "\n" + c.Symbol + "\n" + c.Text;
                string Key(SourceChunk c) => StructuredMemory.Hash($"vectors-v2|{CodeStructure.Version}|{StructuredDocuments.Version}|{c.FileId}|{c.Sha256}|{c.Start}|{c.End}|" + Text(c));
                var needed = snapshot.Chunks.GroupBy(Key).Select(g => g.First()).Where(c => !vectors.ContainsKey(Key(c))).ToArray();
                vectorMisses = needed.Length; vectorHits = snapshot.Chunks.Select(Key).Distinct().Count() - vectorMisses;
                async Task<float[][]> Embed(IReadOnlyList<string> texts)
                {
                    var timer = System.Diagnostics.Stopwatch.StartNew();
                    var result = await embeddings.EmbedAsync(settings.EmbeddingModel, texts, ct);
                    calls.Add(new("embedding:" + identity, texts.Sum(Encoding.UTF8.GetByteCount), timer.Elapsed.TotalMilliseconds, result.Stats));
                    if (result.Vectors.Length != texts.Count || result.Vectors.Any(v => v is not { Length: > 0 and <= 8192 } || v.Any(x => !float.IsFinite(x))))
                        throw new ChatModelException("Dimensions ou valeurs d’embeddings invalides.");
                    return result.Vectors;
                }
                foreach (var batch in needed.Chunk(16))
                {
                    var values = await Embed(batch.Select(Text).ToArray());
                    for (var i = 0; i < batch.Length; i++) vectors[Key(batch[i])] = values[i];
                }
                var keys = snapshot.Chunks.Select(Key).ToHashSet();
                var dirty = !valid || needed.Length > 0 || vectors.Keys.Any(k => !keys.Contains(k));
                vectors = vectors.Where(v => keys.Contains(v.Key)).ToDictionary(v => v.Key, v => v.Value);
                var question = (await Embed([query]))[0];
                if (valid && vectors.Values.Any(v => v.Length != question.Length))
                {
                    // A syntactically valid cache can still contain stale dimensions.
                    vectors.Clear(); dirty = true; vectorHits = 0; vectorMisses = keys.Count;
                    foreach (var batch in snapshot.Chunks.GroupBy(Key).Select(g => g.First()).Chunk(16))
                    {
                        var values = await Embed(batch.Select(Text).ToArray());
                        for (var i = 0; i < batch.Length; i++) vectors[Key(batch[i])] = values[i];
                    }
                }
                similarities = snapshot.Chunks.Select(c => Cosine(question, vectors[Key(c)])).ToArray();
                if (dirty)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                    var temp = path + "." + Guid.NewGuid().ToString("N");
                    try { await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(new VectorCache(2, identity, vectors), ProjectToolCatalog.Json), ct); File.Move(temp, path, true); vectorWritten = true; }
                    finally { if (File.Exists(temp)) File.Delete(temp); }
                }
            }
            finally { EmbeddingGate.Release(); }
        }
        timer.Restart();
        var cachedLexical = _lexical.Get(conversation, snapshot.Chunks);
        var lexical = cachedLexical.Index.Score(query);
        var ranked = ProjectRetrieval.Rank(snapshot.Chunks, query, similarities, lexical);
        var retained = ranked.Select(ProjectRetrieval.Reference).ToHashSet(StringComparer.Ordinal);
        var decisions = snapshot.Chunks.Select((c, i) => new RetrievalDecision(ProjectRetrieval.Reference(c), c.FileId,
            c.Sha256, c.Start, c.End, lexical[i], retained.Contains(ProjectRetrieval.Reference(c)) ? lexical[i] > 0 ? "lexical_match" : "semantic_match"
                : lexical[i] > 0 ? "duplicate_filtered" : "no_match_filtered")).Take(256).ToArray();
        return new(ranked,
            new(settings.RetrievalMode, snapshot.Chunks.Count, snapshot.IndexedFiles, snapshot.EligibleFiles, snapshot.Omissions,
                lexical.Count(s => s == 0), indexMilliseconds, timer.Elapsed.TotalMilliseconds, decisions,
                Math.Max(0, snapshot.Chunks.Count - decisions.Length), new(cachedLexical.Hit, vectorHits, vectorMisses, vectorWritten)), calls);
    }
    public static double Cosine(float[] a, float[] b)
    {
        if (a.Length != b.Length || a.Length == 0 || a.Any(x => !float.IsFinite(x)) || b.Any(x => !float.IsFinite(x)))
            throw new ChatModelException("Cache vectoriel incompatible. Réindexez avec le modèle choisi.");
        double dot = 0, left = 0, right = 0;
        for (var i = 0; i < a.Length; i++) { dot += (double)a[i] * b[i]; left += (double)a[i] * a[i]; right += (double)b[i] * b[i]; }
        return left == 0 || right == 0 ? 0 : dot / Math.Sqrt(left * right);
    }
}
