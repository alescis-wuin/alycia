using Alycia.Modules.Chat.Application;

namespace Alycia.Modules.Chat.Infrastructure;

public sealed class ProjectRetrievalService(IProjectIndex index, IIndexedEmbeddings? semantic = null, LexicalIndexCache? lexicalCache = null) : IProjectRetrieval
{
    private readonly LexicalIndexCache _lexical = lexicalCache ?? new();
    public async Task<RetrievalPreparation> PrepareAsync(Guid conversation, string query, ChatOptions settings, CancellationToken ct)
    {
        var timer = System.Diagnostics.Stopwatch.StartNew();
        var snapshot = await index.BuildAsync(conversation, ct);
        var indexMilliseconds = timer.Elapsed.TotalMilliseconds;
        var calls = new List<ModelCallUsage>();
        double[]? similarities = null;
        var vectorHits = 0; var vectorMisses = 0; var vectorWritten = false;
        var omissions = snapshot.Omissions.ToList();
        if (settings.RetrievalMode == "hybrid" && snapshot.Chunks.Count > 0)
        {
            if (semantic is null) omissions.Add("Index sémantique indisponible ; recherche lexicale utilisée.");
            else
            {
                var found = await semantic.SearchAsync(conversation, query, settings, snapshot.Chunks, ct);
                similarities = found.Scores;
                vectorHits = found.Hits; vectorMisses = snapshot.Chunks.Count - found.Hits;
                calls.AddRange(found.Calls);
                if (found.Warning is not null) omissions.Add(found.Warning);
                if (vectorMisses > 0) omissions.Add($"Indexation sémantique en cours ou à relancer : {found.Hits}/{snapshot.Chunks.Count} passages prêts. Recherche lexicale complète disponible.");
            }
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
            new(settings.RetrievalMode, snapshot.Chunks.Count, snapshot.IndexedFiles, snapshot.EligibleFiles, omissions,
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
