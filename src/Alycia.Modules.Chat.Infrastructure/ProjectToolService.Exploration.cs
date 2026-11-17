using System.Text.Json;
using Alycia.Modules.Chat.Application;

namespace Alycia.Modules.Chat.Infrastructure;

public sealed partial class ProjectToolService
{
    private async Task<ProjectToolResult> ExploreAsync(Guid conversation, string tool, JsonElement args, int budget, CancellationToken ct)
    {
        var query = Query(args);
        var offset = Int(args, "offset", 0, 0, 100000);
        var limit = Int(args, "limit", 8, 1, 20);
        var snapshot = await (index ?? new ProjectIndexService(database, reader.Storage, reader)).BuildAsync(conversation, ct);
        var lexical = ProjectRetrieval.Bm25(snapshot.Chunks, query);
        var scores = snapshot.Chunks.Select((c, i) => (c, score: lexical[i])).ToDictionary(x => ProjectRetrieval.Reference(x.c), x => x.score);
        var ranked = tool == "project_search_passages" ? ProjectRetrieval.Rank(snapshot.Chunks, query)
            : snapshot.Chunks.Where(c => c.Kind != "text" && (tool == "project_find_symbols"
                ? c.Symbol.Contains(query, StringComparison.OrdinalIgnoreCase) : c.References.Contains(query, StringComparer.Ordinal))).ToArray();
        var results = new List<object>(); var evidence = new List<ToolEvidence>();
        string Result() => Json(new
        {
            offset,
            nextOffset = offset + results.Count < ranked.Count ? (int?)(offset + results.Count) : null,
            total = ranked.Count,
            coverage = new { snapshot.EligibleFiles, snapshot.IndexedFiles, returnedPassages = results.Count, examinedByModel = "unknown", snapshot.Omissions },
            syntaxOnly = tool != "project_search_passages",
            results
        });
        foreach (var chunk in ranked.Skip(offset).Take(limit))
        {
            ct.ThrowIfCancellationRequested();
            var text = FileContext.Utf8Prefix(chunk.Text, Math.Min(640, Math.Max(64, budget / 4)));
            var end = chunk.Start + text.Length;
            var endLine = chunk.StartLine + text.AsSpan(0, Math.Max(0, text.Length - 1)).Count('\n');
            var reference = ProjectRetrieval.Reference(chunk.FileId, chunk.Sha256, chunk.Start, end);
            results.Add(new
            {
                reference,
                chunk.FileId,
                path = FileContext.Utf8Prefix(chunk.Path, 240),
                chunk.Sha256,
                start = chunk.Start,
                end,
                chunk.StartLine,
                endLine,
                chunk.CompleteSource,
                symbol = FileContext.Utf8Prefix(chunk.Symbol, 160),
                sourceType = chunk.Kind,
                score = scores[ProjectRetrieval.Reference(chunk)],
                scoreKind = tool == "project_search_passages" ? "BM25" : "syntax_match",
                reason = tool,
                text,
                nextOffset = end < chunk.End ? (int?)end : null
            });
            if (Bytes(Result()) > budget) { results.RemoveAt(results.Count - 1); break; }
            evidence.Add(new(chunk.Path, chunk.Sha256, chunk.Start, end < chunk.End ? end : null, chunk.CompleteSource,
                reference, chunk.FileId, end, chunk.StartLine, endLine));
        }
        if (offset < ranked.Count && results.Count == 0) throw new ArgumentException("Budget insuffisant pour un passage ; utilisez project_read.");
        return new(Result(), EvidenceItems: evidence);
    }

    private async Task<ProjectToolResult> ReadManyAsync(Guid conversation, JsonElement args, int budget, CancellationToken ct)
    {
        if (!args.TryGetProperty("reads", out var reads) || reads.ValueKind != JsonValueKind.Array || reads.GetArrayLength() is < 1 or > 4)
            throw new ArgumentException("reads doit contenir de 1 à 4 lectures.");
        var results = new List<JsonElement>(); var evidence = new List<ToolEvidence>();
        foreach (var read in reads.EnumerateArray())
        {
            var result = await CallAsync(conversation, "project_read", read, Math.Max(1024, (budget - 128) / reads.GetArrayLength()), ct);
            results.Add(JsonSerializer.SerializeToElement(new { result.IsError, result = JsonDocument.Parse(result.Text).RootElement.Clone() }, ProjectToolCatalog.Json));
            if (Bytes(Json(new { results })) > budget)
                throw new ArgumentException("Budget global insuffisant ; réduisez le nombre de lectures.");
            if (!result.IsError && result.Evidence is not null) evidence.Add(result.Evidence);
        }
        return new(Json(new { results }), EvidenceItems: evidence);
    }

    private static string Query(JsonElement args)
    {
        if (!args.TryGetProperty("query", out var q) || q.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(q.GetString()) || q.GetString()!.Length is < 2 or > 120 || q.GetString()!.Any(char.IsControl))
            throw new ArgumentException("query doit contenir de 2 à 120 caractères sans contrôle.");
        return q.GetString()!;
    }
}
