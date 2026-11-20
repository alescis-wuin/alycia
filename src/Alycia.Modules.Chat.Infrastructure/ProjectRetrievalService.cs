using Alycia.Modules.Chat.Application;

namespace Alycia.Modules.Chat.Infrastructure;

public sealed class ProjectRetrievalService(IProjectIndex index, IIndexedEmbeddings? semantic = null, LexicalIndexCache? lexicalCache = null, IPassageReranker? reranker = null) : IProjectRetrieval
{
    private readonly LexicalIndexCache _lexical = lexicalCache ?? new();
    public async Task<RetrievalPreparation> PrepareAsync(Guid conversation, string query, ChatOptions settings, CancellationToken ct)
    {
        var timer = System.Diagnostics.Stopwatch.StartNew();
        var snapshot = await index.BuildScopedAsync(conversation, settings.EligibleSourceIds, ct);
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
        var hits = new Dictionary<string, List<QueryHit>>(StringComparer.Ordinal);
        var coordinates = settings.FacetPlanningEnabled ? snapshot.Chunks.Select((chunk, i) => (chunk, i)).ToDictionary(x => x.chunk, x => x.i) : null;
        void Record(int number, IReadOnlyList<SourceChunk> ordered, double[] terms, double[]? vectors)
        {
            if (coordinates is null) return;
            for (var n = 0; n < Math.Min(ordered.Count, settings.RerankCandidateLimit); n++)
            {
                var chunk = ordered[n]; var key = ProjectRetrieval.Reference(chunk);
                if (!hits.TryGetValue(key, out var list)) hits[key] = list = [];
                var position = coordinates[chunk];
                list.Add(new(number, n + 1, terms[position], vectors?[position]));
            }
        }
        Record(0, ranked, lexical, similarities);
        if ((settings.QueryRewriteEnabled || settings.FacetPlanningEnabled || settings.QueryBriefModelEnabled) && settings.SearchPlan is { Queries.Length: > 1 } plan)
        {
            var lists = new List<IReadOnlyList<SourceChunk>> { ranked };
            foreach (var term in plan.Queries.Skip(1))
            {
                ct.ThrowIfCancellationRequested();
                var terms = cachedLexical.Index.Score(term.Text);
                double[]? vectors = null;
                if (term.Kind == "exact")
                {
                    for (var n = 0; n < terms.Length; n++)
                    {
                        var c = snapshot.Chunks[n];
                        if (c.Text.Contains(term.Text, StringComparison.Ordinal) || c.Path.Contains(term.Text, StringComparison.Ordinal)
                            || c.Symbol.Contains(term.Text, StringComparison.Ordinal)) terms[n] += 20;
                        else if (settings.FacetPlanningEnabled) terms[n] = 0;
                    }
                }
                else if (term.Kind != "lexical" && settings.RetrievalMode == "hybrid" && semantic is not null)
                {
                    var result = await semantic.SearchAsync(conversation, term.Text, settings, snapshot.Chunks, ct);
                    vectors = result.Scores; calls.AddRange(result.Calls);
                    if (result.Warning is not null) omissions.Add(result.Warning);
                }
                var variant = ProjectRetrieval.Rank(snapshot.Chunks, term.Text, vectors, terms);
                Record(lists.Count, variant, terms, vectors);
                lists.Add(variant);
            }
            ranked = lists.SelectMany(list => list.Select((c, rank) => (Chunk: c, Score: 1.0 / (61 + rank))))
                .GroupBy(hit => ProjectRetrieval.Reference(hit.Chunk)).OrderByDescending(g => g.Sum(hit => hit.Score))
                .ThenBy(g => g.First().Chunk.Path, StringComparer.Ordinal).ThenBy(g => g.First().Chunk.Start).Select(g => g.First().Chunk).ToArray();
        }
        if (settings.SearchPlan?.Ambiguous == true) omissions.Add("Référence antérieure ambiguë ou absente ; aucune source supposée.");
        ClassificationReport? classification = null;
        if (settings.ClassificationEnabled)
        {
            var brief = settings.SearchPlan?.Brief ?? PassageClassification.Brief(query, [], []);
            Dictionary<string, double>? semanticScores = similarities is null ? null : snapshot.Chunks.Select((c, n) => (Key: ProjectRetrieval.Reference(c), Score: similarities[n]))
                .GroupBy(p => p.Key).ToDictionary(g => g.Key, g => g.Max(p => p.Score));
            (ranked, classification) = PassageClassification.Apply(query, brief, ranked, settings, semanticScores);
        }
        RerankReport? rerank = null;
        if (settings.RerankEnabled && ranked.Count > 0)
        {
            var elapsed = System.Diagnostics.Stopwatch.StartNew();
            var candidates = ranked.Take(settings.RerankCandidateLimit).ToArray();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(settings.RerankTimeoutMs);
            try
            {
                if (reranker is null) throw new InvalidOperationException();
                var scores = await reranker.ScoreAsync(query, candidates, timeout.Token);
                timeout.Token.ThrowIfCancellationRequested();
                if (scores.Values.Length != candidates.Length || scores.Values.Any(v => !double.IsFinite(v)))
                    throw new InvalidDataException();
                var order = Enumerable.Range(0, candidates.Length).OrderByDescending(n => scores.Values[n]).ThenBy(n => n);
                ranked = order.Select(n => candidates[n]).Concat(ranked.Skip(candidates.Length)).ToArray();
                rerank = new("applied", scores.Identity, elapsed.Elapsed.TotalMilliseconds, candidates
                    .Select((c, n) => (Ref: ProjectRetrieval.Reference(c), Score: scores.Values[n])).ToDictionary(p => p.Ref, p => p.Score));
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex) when (ex is not (OutOfMemoryException or AccessViolationException))
            {
                rerank = new(timeout.IsCancellationRequested ? "timeout" : "unavailable", null, elapsed.Elapsed.TotalMilliseconds);
                omissions.Add("Reclassement local indisponible ou saturé. Ordre de recherche initial conservé.");
            }
        }
        if (settings.PinnedSourceIds is { Count: > 0 } pins)
        {
            var represented = ranked.Select(c => c.FileId).ToHashSet();
            ranked = ranked.Concat(snapshot.Chunks.Where(c => pins.Contains(c.FileId) && !represented.Contains(c.FileId))
                .DistinctBy(c => c.FileId)).ToArray();
        }
        var retained = ranked.Select(ProjectRetrieval.Reference).ToHashSet(StringComparer.Ordinal);
        var decisions = snapshot.Chunks.Select((c, i) => new RetrievalDecision(ProjectRetrieval.Reference(c), c.FileId,
            c.Sha256, c.Start, c.End, lexical[i], retained.Contains(ProjectRetrieval.Reference(c)) ? lexical[i] > 0 ? "lexical_match" : "semantic_match"
                : lexical[i] > 0 ? (classification is null ? "duplicate_filtered" : "rank_or_class_filtered") : "no_match_filtered", hits.GetValueOrDefault(ProjectRetrieval.Reference(c))?.ToArray())).Take(256).ToArray();
        return new(ranked,
            new(settings.RetrievalMode, snapshot.Chunks.Count, snapshot.IndexedFiles, snapshot.EligibleFiles, omissions,
                lexical.Count(s => s == 0), indexMilliseconds, timer.Elapsed.TotalMilliseconds, decisions,
                Math.Max(0, snapshot.Chunks.Count - decisions.Length), new(cachedLexical.Hit, vectorHits, vectorMisses, vectorWritten), rerank, settings.SearchPlan, classification), calls);
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
