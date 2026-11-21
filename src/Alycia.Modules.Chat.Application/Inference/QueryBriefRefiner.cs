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
            var (identity, generation) = await InferenceCall.PrepareAuxiliaryAsync(model, config.Model,
                config.InputTokens, Math.Min(256, config.OutputTokens), 128, config.TimeoutSeconds, "query-brief", schema, timeout.Token);
            if (identity is null || PromptTokens.Count(instruction + brief.Original, generation.TokenCounter) + 64
                > generation.Generation.ContextTokens - generation.Generation.MaxOutputTokens - generation.SafetyTokens) return brief;
            var output = await model.GenerateAsync(config.Model, [new("system", instruction), new("user", brief.Original)],
                generation.CheckWindow(), timeout.Token);
            if (output.Length > 2048) return brief;
            using var document = JsonDocument.Parse(output);
            if (document.RootElement.ValueKind != JsonValueKind.Object || document.RootElement.EnumerateObject().Count() != 1
                || !document.RootElement.TryGetProperty("intent", out var intent) || intent.ValueKind != JsonValueKind.String) return brief;
            var summary = intent.GetString()!.Trim();
            if (summary.Length is < 2 or > 240 || !brief.Original.Contains(summary, StringComparison.Ordinal)) return brief;
            return brief with { Summary = string.Join(" ", new[] { summary }.Concat(brief.Constraints ?? []).Concat(brief.Literals).Distinct()), Method = identity };
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception error) when (error is ChatModelException or HttpRequestException or OperationCanceledException or JsonException) { return brief; }
    }
}
