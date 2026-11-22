using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Alycia.Modules.Chat.Application;

if (args.Length == 2 && args[0] == "--classify") { OfflineEvaluation.Classify(args[1]); return; }
if (args.Length == 2 && args[0] == "--claims") { OfflineEvaluation.Claims(args[1]); return; }
if (args.Length == 3 && args[0] == "--documents") { DocumentBenchmark.Run(args[1], args[2]); return; }
if (args.Length is 2 or 3 && args[0] == "--offline")
{
    await OfflineEvaluation.RunAsync(args[1], args.Length == 3 ? args[2] : null);
    return;
}
const int repeats = 24;
var cases = new List<object>();
foreach (var count in new[] { 64, 512, 2048 })
{
    var chunks = Enumerable.Range(0, count).Select(i =>
    {
        var file = new Guid(i / 4 + 1, 0, 0, new byte[8]);
        var text = string.Join('\n', Enumerable.Range(0, 12).Select(n => $"module {i} block {n} Store ReadAsync Orion quota {i % 7} service transaction"));
        return new SourceChunk(file, $"src/Module{i / 4:D4}/Store.cs", StructuredMemory.Hash(text), $"Store.Read{i}Async", "method",
            i % 4 * 1000, i % 4 * 1000 + text.Length, i % 4 * 12 + 1, i % 4 * 12 + 12, text, ["Store"], true);
    }).ToArray();
    var cache = new LexicalIndexCache(); var conversation = Guid.NewGuid();
    var coldTimer = Stopwatch.StartNew(); cache.Get(conversation, chunks); var cold = coldTimer.Elapsed.TotalMilliseconds;
    string[] queries = ["Store ReadAsync Orion", "quota service transaction", "module 17", "Neptune badminton"];
    foreach (var query in queries)
    {
        var expected = ReferenceAlgorithms.Bm25(chunks, query);
        var actual = cache.Get(conversation, chunks).Index.Score(query);
        if (!expected.SequenceEqual(actual)) throw new InvalidOperationException("BM25 scores differ.");
        var previousOrder = ReferenceAlgorithms.Rank(chunks, query).Select(c => (c.FileId, c.Start));
        var currentOrder = PassageSearch.Rank(chunks, query, lexicalScores: actual).Select(c => (c.FileId, c.Start));
        if (!previousOrder.SequenceEqual(currentOrder)) throw new InvalidOperationException("Diversity ordering differs.");
    }
    var baseline = new List<double>(); var candidate = new List<double>(); var rankBefore = new List<double>(); var rankAfter = new List<double>();
    long allocatedBefore = 0, allocatedAfter = 0;
    for (var i = 0; i < repeats; i++)
    {
        var query = queries[i % queries.Length];
        // Alternate the measurement order to reduce systematic warmup bias.
        void Baseline() { var start = GC.GetAllocatedBytesForCurrentThread(); var timer = Stopwatch.StartNew(); ReferenceAlgorithms.Bm25(chunks, query); baseline.Add(timer.Elapsed.TotalMilliseconds); allocatedBefore += GC.GetAllocatedBytesForCurrentThread() - start; }
        void Candidate() { var start = GC.GetAllocatedBytesForCurrentThread(); var timer = Stopwatch.StartNew(); cache.Get(conversation, chunks).Index.Score(query); candidate.Add(timer.Elapsed.TotalMilliseconds); allocatedAfter += GC.GetAllocatedBytesForCurrentThread() - start; }
        if (i % 2 == 0) { Baseline(); Candidate(); } else { Candidate(); Baseline(); }
        var timer = Stopwatch.StartNew(); ReferenceAlgorithms.Rank(chunks, query); rankBefore.Add(timer.Elapsed.TotalMilliseconds);
        timer.Restart(); PassageSearch.Rank(chunks, query, lexicalScores: cache.Get(conversation, chunks).Index.Score(query)); rankAfter.Add(timer.Elapsed.TotalMilliseconds);
    }
    cases.Add(new
    {
        chunks = count,
        files = count / 4,
        characters = chunks.Sum(c => c.Text.Length),
        cold_index_ms = cold,
        repeats,
        scores_identical = true,
        ordering_identical = true,
        baseline_bm25 = Statistics(baseline),
        cached_bm25 = Statistics(candidate),
        baseline_ranking = Statistics(rankBefore),
        cached_ranking = Statistics(rankAfter),
        baseline_bm25_allocated_bytes_per_call = allocatedBefore / repeats,
        cached_bm25_allocated_bytes_per_call = allocatedAfter / repeats
    });
}
var result = new
{
    schema_version = 1,
    measured_at_utc = DateTimeOffset.UtcNow,
    runtime = RuntimeInformation.FrameworkDescription,
    os = RuntimeInformation.OSDescription,
    processors = Environment.ProcessorCount,
    baseline_bm25_commit = "0b31f13",
    baseline_ranking_commit = "cdb5fee",
    scope = "Synthetic CPU microbenchmark; full fingerprint lookup included; no disk I/O, Ollama, GPU, end-to-end latency or quality measurement.",
    cases
};
var json = JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
if (args.Length > 0) { Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(args[0]))!); File.WriteAllText(args[0], json + "\n"); }
Console.WriteLine(json);
static object Statistics(List<double> values)
{
    var sorted = values.Order().ToArray();
    return new { p50_ms = sorted[sorted.Length / 2], p95_ms = sorted[(int)Math.Ceiling(sorted.Length * .95) - 1] };
}
