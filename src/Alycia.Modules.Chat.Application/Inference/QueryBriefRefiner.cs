using System.Text.Json;
using Alycia.Contracts.Retrieval;

namespace Alycia.Modules.Chat.Application;

public sealed class QueryBriefRefiner(IChatModel model) : IQueryBriefRefiner
{
    public async Task<QueryBrief> RefineAsync(QueryBrief brief, BriefGeneration config, CancellationToken ct)
    {
        var schema = JsonSerializer.SerializeToElement(new
        {
            type = "object",
            required = new[] { "intent" },
            additionalProperties = false,
            properties = new { intent = new { type = "string", maxLength = 240 } }
        });
        const string instruction = "Sélectionne un extrait exact et continu de la question exprimant son intention. Ne déduis aucun référent manquant. JSON uniquement.";
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(config.TimeoutSeconds));
            var local = await model.PrepareOptionsAsync(config.Model, new ChatOptions
            {
                CallPurpose = "query-brief",
                ContextTokens = config.InputTokens,
                MaxOutputTokens = Math.Min(256, config.OutputTokens),
                SafetyTokens = 128,
                TimeoutSeconds = config.TimeoutSeconds,
                GenerateUntilStop = false,
                UseProjectTools = false,
                Temperature = 0,
                Seed = 0,
                ResponseSchema = schema
            }, timeout.Token);
            if (local.ModelIdentity is null || TokenBudget.Count(instruction + brief.Original, local) + 64 > local.ContextTokens - local.MaxOutputTokens - local.SafetyTokens) return brief;
            var output = await model.GenerateAsync(config.Model, [new("system", instruction), new("user", brief.Original)], local, timeout.Token);
            if (output.Length > 2048) return brief;
            using var document = JsonDocument.Parse(output);
            if (document.RootElement.ValueKind != JsonValueKind.Object || document.RootElement.EnumerateObject().Count() != 1
                || !document.RootElement.TryGetProperty("intent", out var intent) || intent.ValueKind != JsonValueKind.String) return brief;
            var summary = intent.GetString()!.Trim();
            if (summary.Length is < 2 or > 240 || !brief.Original.Contains(summary, StringComparison.Ordinal)) return brief;
            return brief with { Summary = string.Join(" ", new[] { summary }.Concat(brief.Constraints ?? []).Concat(brief.Literals).Distinct()), Method = local.ModelIdentity };
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception error) when (error is ChatModelException or HttpRequestException or OperationCanceledException or JsonException) { return brief; }
    }
}
