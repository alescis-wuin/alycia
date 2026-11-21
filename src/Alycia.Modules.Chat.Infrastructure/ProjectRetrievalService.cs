using Alycia.Modules.Chat.Application;
using Alycia.Modules.Chat.Application.Retrieval;

namespace Alycia.Modules.Chat.Infrastructure;

public sealed class ProjectRetrievalService(IProjectIndex index, IIndexedEmbeddings? semantic = null, LexicalIndexCache? lexicalCache = null, IPassageReranker? reranker = null) : IProjectRetrieval
{
    private readonly LexicalIndexCache _lexical = lexicalCache ?? new();
    public async Task<RetrievalPreparation> PrepareAsync(Guid conversation, string query, ChatOptions settings, CancellationToken ct)
    {
        settings = settings.Copy();
        var policy = PassageRankingSettings.Capture(settings, settings.PinnedSourceIds);
        var brief = settings.SearchPlan?.Brief;
        var timer = System.Diagnostics.Stopwatch.StartNew();
        var snapshot = await index.BuildScopedAsync(conversation, settings.EligibleSourceIds, ct);
        var indexMilliseconds = timer.Elapsed.TotalMilliseconds;
        var calls = new List<ModelCallUsage>();
        double[]? similarities = null;
        var vectorHits = 0; var vectorMisses = 0;
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
        var ranking = new PassageRanking(snapshot.Chunks, cachedLexical.Index, policy, brief, query, similarities);
        if ((settings.QueryRewriteEnabled || settings.FacetPlanningEnabled || settings.QueryBriefModelEnabled) && settings.SearchPlan is { Queries.Length: > 1 } plan)
        {
            foreach (var term in plan.Queries.Skip(1))
            {
                ct.ThrowIfCancellationRequested();
                double[]? vectors = null;
                if (term.Kind is not ("exact" or "lexical") && settings.RetrievalMode == "hybrid" && semantic is not null)
                {
                    var result = await semantic.SearchAsync(conversation, term.Text, settings, snapshot.Chunks, ct);
                    vectors = result.Scores;
                    calls.AddRange(result.Calls);
                    if (result.Warning is not null) omissions.Add(result.Warning);
                }
                ranking.AddVariant(term, vectors);
            }
        }
        if (settings.SearchPlan?.Ambiguous == true) omissions.Add("Référence antérieure ambiguë ou absente ; aucune source supposée.");
        var resultRanking = await ranking.SelectAsync(reranker, ct);
        if (resultRanking.Rerank?.State is "timeout" or "unavailable")
            omissions.Add("Reclassement local indisponible ou saturé. Ordre de recherche initial conservé.");
        return new(resultRanking.Chunks,
            new(settings.RetrievalMode, snapshot.Chunks.Count, snapshot.IndexedFiles, snapshot.EligibleFiles, omissions,
                resultRanking.ZeroLexicalScores, indexMilliseconds, timer.Elapsed.TotalMilliseconds, resultRanking.Decisions,
                Math.Max(0, snapshot.Chunks.Count - resultRanking.Decisions.Length), new(cachedLexical.Hit, vectorHits, vectorMisses, false),
                resultRanking.Rerank, settings.SearchPlan, resultRanking.Classification), calls);
    }

    public static double Cosine(float[] a, float[] b) => VectorSimilarity.Cosine(a, b);
}
