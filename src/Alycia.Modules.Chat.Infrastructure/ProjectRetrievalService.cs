using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Alycia.Modules.Chat.Application;

namespace Alycia.Modules.Chat.Infrastructure;

public sealed class ProjectRetrievalService(IProjectIndex index, ILocalEmbeddings embeddings, FileStorage storage) : IProjectRetrieval
{
    private sealed record VectorCache(string Identity, Dictionary<string, float[]> Vectors);
    public async Task<RetrievalPreparation> PrepareAsync(Guid conversation, string query, ChatOptions settings, CancellationToken ct)
    {
        var snapshot = await index.BuildAsync(conversation, ct);
        var calls = new List<ModelCallUsage>();
        double[]? similarities = null;
        if (settings.RetrievalMode == "hybrid" && snapshot.Chunks.Count > 0)
        {
            if (snapshot.Chunks.Count > 2048) throw new ArgumentException("Recherche hybride limitée à 2048 passages : ciblez un import plus petit ou choisissez BM25.");
            var identity = await embeddings.IdentityAsync(settings.EmbeddingModel, ct);
            var path = Path.Combine(storage.DirectoryFor(conversation), "vectors-v1.json");
            VectorCache? cache = null;
            if (File.Exists(path) && new FileInfo(path).Length <= 128 * 1024 * 1024)
            {
                try { cache = JsonSerializer.Deserialize<VectorCache>(await File.ReadAllTextAsync(path, ct), ProjectToolCatalog.Json); }
                catch (JsonException) { }
            }
            var vectors = cache?.Identity == identity ? cache.Vectors : new Dictionary<string, float[]>();
            string Text(SourceChunk c) => c.Path + "\n" + c.Symbol + "\n" + c.Text;
            string Key(SourceChunk c) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Text(c))));
            var needed = snapshot.Chunks.GroupBy(Key).Select(g => g.First()).Where(c => !vectors.ContainsKey(Key(c))).ToArray();
            async Task<float[][]> Embed(IReadOnlyList<string> texts)
            {
                var timer = System.Diagnostics.Stopwatch.StartNew();
                var result = await embeddings.EmbedAsync(settings.EmbeddingModel, texts, ct);
                calls.Add(new("embedding:" + identity, texts.Sum(Encoding.UTF8.GetByteCount), timer.Elapsed.TotalMilliseconds, result.Stats));
                return result.Vectors;
            }
            foreach (var batch in needed.Chunk(16))
            {
                var values = await Embed(batch.Select(Text).ToArray());
                for (var i = 0; i < batch.Length; i++) vectors[Key(batch[i])] = values[i];
            }
            var keys = snapshot.Chunks.Select(Key).ToHashSet();
            vectors = vectors.Where(v => keys.Contains(v.Key)).ToDictionary(v => v.Key, v => v.Value);
            var question = (await Embed([query]))[0];
            similarities = snapshot.Chunks.Select(c => Cosine(question, vectors[Key(c)])).ToArray();
            var temp = path + "." + Guid.NewGuid().ToString("N");
            try { await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(new VectorCache(identity, vectors), ProjectToolCatalog.Json), ct); File.Move(temp, path, true); }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
        return new(ProjectRetrieval.Rank(snapshot.Chunks, query, similarities),
            new(settings.RetrievalMode, snapshot.Chunks.Count, snapshot.IndexedFiles, snapshot.EligibleFiles, snapshot.Omissions), calls);
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
