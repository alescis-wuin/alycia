namespace Alycia.Modules.Chat.Application.Retrieval;

public sealed record RankedPassages(IReadOnlyList<SourceChunk> Chunks, RetrievalDecision[] Decisions,
    int ZeroLexicalScores, ClassificationReport? Classification, RerankReport? Rerank);

// All source material belongs to the pre-filtered index snapshot for this search.
public sealed class PassageRanking
{
    private readonly IReadOnlyList<SourceChunk> _chunks;
    private readonly LexicalIndex _index;
    private readonly ChatOptions _settings;
    private readonly string _question;
    private readonly double[]? _similarities;
    private readonly double[] _originalScores;
    private readonly List<IReadOnlyList<SourceChunk>> _rankings = [];
    private readonly Dictionary<string, List<QueryHit>> _hits = new(StringComparer.Ordinal);
    private readonly Dictionary<SourceChunk, int>? _positions;

    public PassageRanking(IReadOnlyList<SourceChunk> chunks, LexicalIndex index, ChatOptions settings,
        string question, double[]? similarities)
    {
        _chunks = chunks;
        _index = index;
        _settings = settings;
        _question = question;
        _similarities = similarities;
        _originalScores = index.Score(question);
        _positions = settings.FacetPlanningEnabled
            ? chunks.Select((chunk, i) => (chunk, i)).ToDictionary(x => x.chunk, x => x.i) : null;
        AddRanking(question, _originalScores, similarities);
    }

    public void AddVariant(SearchTerm term, double[]? similarities)
    {
        var scores = _index.Score(term.Text);
        if (term.Kind == "exact")
        {
            for (var i = 0; i < scores.Length; i++)
            {
                var chunk = _chunks[i];
                if (chunk.Text.Contains(term.Text, StringComparison.Ordinal) || chunk.Path.Contains(term.Text, StringComparison.Ordinal)
                    || chunk.Symbol.Contains(term.Text, StringComparison.Ordinal)) scores[i] += 20;
                else if (_settings.FacetPlanningEnabled) scores[i] = 0;
            }
        }
        AddRanking(term.Text, scores, similarities);
    }

    private void AddRanking(string query, double[] terms, double[]? vectors)
    {
        var ranked = ProjectRetrieval.Rank(_chunks, query, vectors, terms);
        var queryNumber = _rankings.Count;
        _rankings.Add(ranked);
        if (_positions is null) return;
        for (var i = 0; i < Math.Min(ranked.Count, _settings.RerankCandidateLimit); i++)
        {
            var chunk = ranked[i];
            var reference = ProjectRetrieval.Reference(chunk);
            if (!_hits.TryGetValue(reference, out var hits)) _hits[reference] = hits = [];
            var position = _positions[chunk];
            hits.Add(new(queryNumber, i + 1, terms[position], vectors?[position]));
        }
    }

    public async Task<RankedPassages> SelectAsync(IPassageReranker? reranker, CancellationToken ct)
    {
        var ranked = _rankings.Count == 1 ? _rankings[0] : Fuse();
        ClassificationReport? classification = null;
        if (_settings.ClassificationEnabled)
        {
            var brief = _settings.SearchPlan?.Brief ?? PassageClassification.Brief(_question, [], []);
            var semanticScores = _similarities is null ? null : _chunks.Select((c, i) => (Key: ProjectRetrieval.Reference(c), Score: _similarities[i]))
                .GroupBy(p => p.Key).ToDictionary(g => g.Key, g => g.Max(p => p.Score));
            (ranked, classification) = PassageClassification.Apply(_question, brief, ranked, _settings, semanticScores);
        }
        var (ordered, rerank) = await RerankAsync(ranked, reranker, ct);
        if (_settings.PinnedSourceIds is { Count: > 0 } pins)
        {
            var represented = ordered.Select(c => c.FileId).ToHashSet();
            ordered = ordered.Concat(_chunks.Where(c => pins.Contains(c.FileId) && !represented.Contains(c.FileId))
                .DistinctBy(c => c.FileId)).ToArray();
        }
        return new(ordered, Describe(ordered, classification is not null), _originalScores.Count(s => s == 0), classification, rerank);
    }

    private IReadOnlyList<SourceChunk> Fuse() =>
        _rankings.SelectMany(list => list.Select((chunk, rank) => (Chunk: chunk, Score: 1.0 / (61 + rank))))
            .GroupBy(hit => ProjectRetrieval.Reference(hit.Chunk)).OrderByDescending(g => g.Sum(hit => hit.Score))
            .ThenBy(g => g.First().Chunk.Path, StringComparer.Ordinal).ThenBy(g => g.First().Chunk.Start).Select(g => g.First().Chunk).ToArray();

    private async Task<(IReadOnlyList<SourceChunk> Chunks, RerankReport? Report)> RerankAsync(
        IReadOnlyList<SourceChunk> ranked, IPassageReranker? reranker, CancellationToken ct)
    {
        var limits = _settings.Ranking;
        if (!limits.Enabled || ranked.Count == 0) return (ranked, null);
        var elapsed = System.Diagnostics.Stopwatch.StartNew();
        var candidates = ranked.Take(limits.Candidates).ToArray();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(limits.TimeoutMs);
        try
        {
            if (reranker is null) throw new InvalidOperationException();
            var scores = await reranker.ScoreAsync(_question, candidates, timeout.Token);
            timeout.Token.ThrowIfCancellationRequested();
            if (scores.Values.Length != candidates.Length || scores.Values.Any(v => !double.IsFinite(v)))
                throw new InvalidDataException();
            var order = Enumerable.Range(0, candidates.Length).OrderByDescending(i => scores.Values[i]).ThenBy(i => i);
            var ordered = order.Select(i => candidates[i]).Concat(ranked.Skip(candidates.Length)).ToArray();
            return (ordered, new("applied", scores.Identity, elapsed.Elapsed.TotalMilliseconds, candidates
                .Select((c, i) => (Ref: ProjectRetrieval.Reference(c), Score: scores.Values[i])).ToDictionary(p => p.Ref, p => p.Score)));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex) when (ex is not (OutOfMemoryException or AccessViolationException))
        {
            return (ranked, new(timeout.IsCancellationRequested ? "timeout" : "unavailable", null, elapsed.Elapsed.TotalMilliseconds));
        }
    }

    private RetrievalDecision[] Describe(IReadOnlyList<SourceChunk> ranked, bool classified)
    {
        var retained = ranked.Select(ProjectRetrieval.Reference).ToHashSet(StringComparer.Ordinal);
        return _chunks.Take(256).Select((chunk, i) =>
        {
            var reference = ProjectRetrieval.Reference(chunk);
            var matched = _originalScores[i] > 0;
            var reason = retained.Contains(reference) ? matched ? "lexical_match" : "semantic_match"
                : matched ? classified ? "rank_or_class_filtered" : "duplicate_filtered" : "no_match_filtered";
            return new RetrievalDecision(reference, chunk.FileId, chunk.Sha256, chunk.Start, chunk.End,
                _originalScores[i], reason, _hits.GetValueOrDefault(reference)?.ToArray());
        }).ToArray();
    }
}
