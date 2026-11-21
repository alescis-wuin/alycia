using System.Diagnostics;
using System.Text.Json;
using Alycia.Modules.Chat.Application;
using Alycia.Modules.Chat.Infrastructure;

internal static class DocumentBenchmark
{
    public static void Run(string corpus, string output)
    {
        var rows = new List<object>();
        foreach (var path in Directory.EnumerateFiles(corpus, "*", SearchOption.AllDirectories).Where(StructuredDocuments.Supports).Order())
        {
            var bytes = File.ReadAllBytes(path); var name = Path.GetRelativePath(corpus, path); var id = Guid.NewGuid();
            var extracted = StructuredDocuments.Extract(name, bytes);
            CodeStructure.Split(id, name, "benchmark", extracted.Text, default);
            var extraction = new List<double>(); var splitting = new List<double>();
            for (var i = 0; i < 24; i++)
            {
                var timer = Stopwatch.StartNew(); var document = StructuredDocuments.Extract(name, bytes); extraction.Add(timer.Elapsed.TotalMilliseconds);
                timer.Restart(); CodeStructure.Split(id, name, "benchmark", document.Text, default); splitting.Add(timer.Elapsed.TotalMilliseconds);
            }
            rows.Add(new
            {
                path = name,
                bytes = bytes.Length,
                characters = extracted.Text.Length,
                complete = extracted.Complete,
                repeats = 24,
                extraction = Stats(extraction),
                segmentation = Stats(splitting)
            });
        }
        var json = JsonSerializer.Serialize(new
        {
            measured_at_utc = DateTimeOffset.UtcNow,
            scope = "Warm CPU extraction/segmentation on frozen synthetic documents, bytes already in memory; no database, upload, OCR or model inference.",
            rows
        }, new JsonSerializerOptions { WriteIndented = true });
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!); File.WriteAllText(output, json + "\n"); Console.WriteLine(json);
    }
    private static object Stats(List<double> values)
    {
        var ordered = values.Order().ToArray(); return new { p50_ms = ordered[12], p95_ms = ordered[22] };
    }
}
