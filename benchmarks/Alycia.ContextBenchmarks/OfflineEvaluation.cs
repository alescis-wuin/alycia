using Alycia.Modules.Documents;
using Alycia.Contracts.Retrieval;
using Alycia.Modules.Retrieval;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Alycia.Modules.Chat.Application;
using Alycia.Modules.Chat.Domain;
using Alycia.Modules.Chat.Infrastructure;

internal static class OfflineEvaluation
{
    private sealed record Annotation(string Id, string Source, string Claim, string Status, string? Role);
    public static void Claims(string path)
    {
        var json = new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true };
        var bytes = File.ReadAllBytes(path);
        var annotations = JsonSerializer.Deserialize<Annotation[]>(bytes, json)!;
        var rows = annotations.Select(a =>
        {
            var reference = a.Role is null ? "SABCDEF123456" : "H1:ABCDEF123456";
            var claim = a.Claim.Replace("{ref}", "[" + reference + "]");
            EvidenceText[] evidence = a.Source.Length == 0 ? [] : [new(reference, a.Source, a.Role)];
            var legacy = ClaimAudit.Check(claim, [], evidence).Findings.FirstOrDefault()?.Status;
            var actual = SemanticClaimAudit.Check(claim, [], evidence).Findings.Single().Status;
            return new { a.Id, expected = a.Status, actual, legacy };
        }).ToArray();
        var contradictions = rows.Count(r => r.expected == "contradicted");
        var other = rows.Length - contradictions;
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            sha256 = Convert.ToHexString(SHA256.HashData(bytes)),
            samples = rows.Length,
            correct = rows.Count(r => r.expected == r.actual),
            contradictions,
            others = other,
            detected = rows.Count(r => r.expected == "contradicted" && r.actual == "contradicted"),
            falseAlerts = rows.Count(r => r.expected != "contradicted" && r.actual == "contradicted"),
            rows
        }, json));
    }

    private sealed record Pair(string Id, string Question, string Text, string Expected, string Relation);
    public static void Classify(string path)
    {
        var json = new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true };
        var bytes = File.ReadAllBytes(path);
        var pairs = JsonSerializer.Deserialize<Pair[]>(bytes, json)!;
        var options = new ChatOptions();
        var rows = pairs.Select(p =>
        {
            var doc = new SourceChunk(Guid.Empty, "", StructuredMemory.Hash(p.Text), "", "text", 0, p.Text.Length, 1, 1, p.Text, [], true);
            var brief = QueryPlanning.Build(p.Question, [], [], 4, true).Brief!;
            var verdict = PassageClassification.Apply(p.Question, brief, [doc], options).Report.Candidates.Single();
            return new { p.Id, p.Expected, actual = verdict.Relevance, expectedRelation = p.Relation, verdict.Relation, verdict.Admitted };
        }).ToArray();
        string[] classes = ["relevant", "uncertain_or_neutral", "irrelevant"];
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            sha256 = Convert.ToHexString(SHA256.HashData(bytes)),
            samples = rows.Length,
            thresholds = new { options.RelevantThreshold, options.NeutralThreshold },
            classes,
            matrix = classes.Select(expected => classes.Select(actual => rows.Count(r => r.Expected == expected && r.actual == actual))),
            correct = rows.Count(r => r.Expected == r.actual),
            missedRelevant = rows.Where(r => r.Expected == "relevant" && !r.Admitted).Select(r => r.Id),
            relationCorrect = rows.Count(r => r.expectedRelation == r.Relation),
            rows
        }, json));
    }

    private sealed record Case(string Id, string Question, string[] Expected, string[] Turns, string[][]? FacetTargets = null);
    private sealed record Suite(int Version, int K, int Budget, string[][] Documents, Case[] Cases, string[] Excluded);
    private sealed class Index(SourceChunk[] chunks, int files) : IProjectIndex
    {
        public Task<ProjectIndexSnapshot> BuildAsync(Guid id, CancellationToken ct) =>
            Task.FromResult(new ProjectIndexSnapshot(chunks, files, files, files, []));
    }
    private sealed class FixtureRepository(Attachment[] documents, ChatMessage[] history) : IChatRepository
    {
        public Task<IReadOnlyList<Attachment>> FilesAsync(Guid id, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<Attachment>>(documents.Where(f => f.ConversationId == id).ToArray());
        public Task<IReadOnlyList<ChatMessage>> MessagesAsync(Guid id, int limit, int? before, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<ChatMessage>>(history.Where(m => m.ConversationId == id && (before == null || m.Sequence < before)).TakeLast(limit).ToArray());
        public Task<IReadOnlyList<Conversation>> ListAsync(CancellationToken ct) => throw new NotSupportedException();
        public Task<Conversation?> FindAsync(Guid id, CancellationToken ct) => throw new NotSupportedException();
        public Task AddAsync(Conversation c, CancellationToken ct) => throw new NotSupportedException();
        public Task SaveAsync(CancellationToken ct) => throw new NotSupportedException();
        public Task DeleteAsync(Conversation c, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<ChatMessage>> FindTurnAsync(Guid id, Guid request, CancellationToken ct) => throw new NotSupportedException();
        public Task AppendTurnAsync(Conversation c, ChatMessage u, ChatMessage r, CancellationToken ct) => throw new NotSupportedException();
    }
    private sealed class NoInference : IChatModel
    {
        public Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken ct) => throw new NotSupportedException();
        public Task<string> GenerateAsync(string model, IReadOnlyList<PromptMessage> messages, CancellationToken ct) => throw new NotSupportedException();
    }
    public static async Task RunAsync(string suitePath, string? settingsPath)
    {
        var json = new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true };
        var data = await File.ReadAllBytesAsync(suitePath);
        var suite = JsonSerializer.Deserialize<Suite>(data, json)!;
        var settings = settingsPath is null ? new ChatOptions() : JsonSerializer.Deserialize<ChatOptions>(await File.ReadAllTextAsync(settingsPath), json)!;
        settings.Validate();
        var owner = Conversation.Create("Fixture", "local", "");
        var conversation = owner.Id;
        var files = suite.Documents.Select(d => new Attachment
        {
            Id = new Guid(SHA256.HashData(Encoding.UTF8.GetBytes(d[0]))[..16]),
            Name = d[0],
            Sha256 = StructuredMemory.Hash(d[1]),
            Text = d[1],
            ContextMode = suite.Excluded.Contains(d[0]) ? "excluded" : "project",
            ConversationId = conversation
        }).ToArray();
        var allowed = files.Where(f => f.ContextMode != "excluded").ToArray();
        var chunks = allowed.SelectMany(f => CodeStructure.Split(f.Id, f.Name, f.Sha256, f.Text, hierarchical: settings.HierarchicalIndexEnabled)).ToArray();
        using var reranker = new OnnxPassageReranker(Environment.GetEnvironmentVariable("ALYCIA_RERANK_DIRECTORY"));
        var service = new ProjectRetrievalService(new RetrievalPipeline(new Index(chunks, allowed.Length), new(), reranker));
        var rows = new List<object>();
        var recalls = new List<double>(); var ndcgs = new List<double>(); var reciprocal = new List<double>();
        var elapsed = new List<double>(); var empty = new List<bool>(); var costs = new List<int>();
        foreach (var item in suite.Cases)
        {
            var options = settings.Copy();
            var allocated = GC.GetTotalAllocatedBytes();
            var timer = Stopwatch.StartNew();
            var history = item.Turns.Select((text, n) => new ChatMessage(conversation, n + 1, Guid.NewGuid(), "user", text)).ToArray();
            await QueryRouting.PrepareAsync(new FixtureRepository(files, history), owner, item.Question, options, service, new(new NoInference()), default);
            var packed = ProjectRetrieval.Pack(options.SourceCandidates ?? [], files, suite.Budget, options);
            elapsed.Add(timer.Elapsed.TotalMilliseconds);
            var paths = packed.Sources.Select(s => s.Path).Distinct().Take(suite.K).ToArray();
            var hits = paths.Select((p, i) => item.Expected.Contains(p) ? i + 1 : 0).Where(i => i > 0).ToArray();
            var tokens = packed.Text.Length == 0 ? 0 : TokenBudget.Count(packed.Text, options);
            if (paths.Intersect(suite.Excluded).Any() || tokens > suite.Budget) throw new InvalidDataException("Portée ou budget violé : " + item.Id);
            double? recall = null, ndcg = null, rr = null;
            if (item.Expected.Length > 0)
            {
                recall = (double)hits.Length / item.Expected.Length;
                ndcg = hits.Sum(r => 1 / Math.Log2(r + 1)) / Enumerable.Range(1, Math.Min(item.Expected.Length, suite.K)).Sum(r => 1 / Math.Log2(r + 1));
                rr = hits.Length == 0 ? 0 : 1.0 / hits[0];
                recalls.Add(recall.Value); ndcgs.Add(ndcg.Value); reciprocal.Add(rr.Value);
            }
            else empty.Add(paths.Length == 0);
            costs.Add(tokens);
            rows.Add(new
            {
                item.Id,
                paths,
                item.Expected,
                recall,
                ndcg,
                rr,
                tokens,
                route = options.Route?.Mode,
                rerank = options.RetrievalReport?.Rerank,
                allocatedBytes = Math.Max(0, GC.GetTotalAllocatedBytes() - allocated),
                facetsExpected = item.FacetTargets?.Length,
                facetsCovered = item.FacetTargets?.Count(group => paths.Intersect(group).Any()),
                classifications = options.RetrievalReport?.Classification?.Candidates.Length
            });
        }
        var latency = elapsed.Order().ToArray();
        double Percentile(double p) => latency[(int)Math.Ceiling(p * latency.Length) - 1];
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            suite.Version,
            sha256 = Convert.ToHexString(SHA256.HashData(data)),
            suite.K,
            samples = rows.Count,
            positive = recalls.Count,
            negative = empty.Count,
            meanRecall = recalls.Average(),
            meanNdcg = ndcgs.Average(),
            mrr = reciprocal.Average(),
            emptyNegative = empty.Count(x => x),
            contextTokens = costs.Sum(),
            p50Ms = Percentile(.5),
            p95Ms = Percentile(.95),
            runtime = Environment.Version.ToString(),
            rows
        }, json));
    }
}
