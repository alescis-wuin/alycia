namespace Alycia.Modules.Chat.Application;

public static class ContextGuides
{
    public static async Task<ContextWindow> AddAsync(ContextWindow window, ChatOptions settings, IModelMeter model,
        bool generate, CancellationToken ct)
    {
        if (settings.QuerySummaryTokens == 0 || settings.SourceCandidates is not { Count: > 0 } candidates
            || window.Sources is not { Count: > 0 } selected) return window;
        var maximum = settings.ContextTokens - settings.SafetyTokens - settings.MaxOutputTokens;
        var prompt = window.Messages.ToArray();
        var cost = Math.Max(window.EstimatedInputTokens, prompt.Sum(m => TokenBudget.Count(m.Content, settings)));
        var allowance = Math.Min(settings.QuerySummaryTokens, maximum - cost);
        if (settings.DocumentContextTokens > 0)
        {
            var originalCost = window.Assembly?.Used ?? selected.Sum(s => TokenBudget.Count(s.Content ?? "", settings));
            allowance = Math.Min(allowance, settings.DocumentContextTokens - originalCost);
        }
        var guides = new List<UsedGuide>();
        var added = 0; var calls = new IndexCallBudget(settings.QuerySummaryMaxCalls);
        foreach (var source in selected.Where(s => s.Reference is not null))
        {
            ct.ThrowIfCancellationRequested();
            var chunk = candidates.FirstOrDefault(c => c.FileId == source.Id && c.Sha256 == source.Sha256 && c.Start == source.Start);
            if (chunk is null || allowance - added < 96) continue;
            var cached = chunk.Guide;
            var summary = cached?.Summary;
            var identity = cached?.Identity; var state = cached?.State;
            if (summary is null && generate && calls.Remaining > 0 && settings.Enrichment.Model.Length > 0
                && TokenBudget.Count(chunk.Text, settings) > allowance - added)
            {
                ChatOptions local;
                try
                {
                    local = await model.PrepareOptionsAsync(settings.Enrichment.Model, new ChatOptions
                    {
                        CallPurpose = "summary",
                        ContextTokens = settings.Enrichment.InputTokens,
                        MaxOutputTokens = settings.Enrichment.OutputTokens,
                        GenerateUntilStop = false,
                        Temperature = 0,
                        Seed = 0,
                        UseProjectTools = false,
                        TimeoutSeconds = settings.Enrichment.TimeoutSeconds
                    }, ct);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                catch (Exception ex) when (ex is ChatModelException or HttpRequestException or OperationCanceledException) { continue; }
                if (local.ModelIdentity is null) continue;
                try
                {
                    summary = await ReduceAsync(chunk.Text, settings.Enrichment, local, Math.Min(settings.Enrichment.TargetTokens, allowance - added), calls, model, ct);
                    identity = local.ModelIdentity; state = "on_demand";
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                catch (Exception ex) when (ex is ChatModelException or HttpRequestException or OperationCanceledException) { continue; }
            }
            if (string.IsNullOrWhiteSpace(summary)) continue;
            var original = SourceCoordinates.Reference(chunk);
            var scope = original == source.Reference ? "extrait complet ci-dessus" : "unité complète, extrait ci-dessus partiel";
            var heading = $"\nSynthèse dérivée · {source.Path} · {scope} · vérifier les affirmations dans le passage fourni [{source.Reference}]\n";
            var text = IndexedKnowledge.Fit(summary, allowance - added - TokenBudget.Count(heading, settings), settings);
            if (text.Length == 0) continue;
            var last = prompt[^1];
            var extended = last.Content + heading + text;
            var increase = TokenBudget.Count(extended, settings) - TokenBudget.Count(last.Content, settings);
            if (added + increase > allowance || cost + increase > maximum) continue;
            prompt[^1] = last with { Content = extended }; cost += increase; added += increase;
            guides.Add(new(source.Reference!, SourceCoordinates.Reference(chunk), chunk.FileId, identity!, state!, increase,
                cached?.Created ?? source.Created, cached?.Modified ?? source.Modified, cached?.Uploaded ?? source.Uploaded, cached?.NodeKey, chunk.Sha256, chunk.Path, chunk.Start, chunk.End, text.Length < summary.Length || cached?.Partial == true));
        }
        return guides.Count == 0 ? window : window with { Messages = prompt, EstimatedInputTokens = cost, Guides = guides.ToArray() };
    }

    private static async Task<string?> ReduceAsync(string text, EnrichmentOptions config, ChatOptions local, int target,
        IndexCallBudget calls, IModelMeter model, CancellationToken ct)
    {
        var instruction = $"Résume les données fournies en {target} tokens maximum. Conserve incertitudes, désaccords et termes exacts.";
        var perInput = local.ContextTokens - local.SafetyTokens - local.MaxOutputTokens - TokenBudget.Count(instruction, local) - 64;
        for (var level = 0; level < config.MaxDepth && calls.Remaining > 0; level++)
        {
            var parts = new List<string>();
            for (var offset = 0; offset < text.Length;)
            {
                var part = IndexedKnowledge.Fit(text[offset..], perInput, local);
                if (part.Length == 0 || parts.Count >= calls.Remaining) return null;
                parts.Add(part); offset += part.Length;
            }
            if (parts.Count > 1 && (parts.Count + 1 > calls.Remaining || level + 1 == config.MaxDepth)) return null;
            var reduced = new List<string>();
            foreach (var part in parts)
            {
                ct.ThrowIfCancellationRequested();
                if (!calls.TryReserve()) return null;
                var reply = (await model.GenerateAsync(config.Model, [new("system", instruction), new("user", part)], local.InferenceCall(), ct)).Trim();
                if (reply.Length == 0 || reply.Contains('\0') || TokenBudget.Count(reply, local) > target) return null;
                reduced.Add(reply);
            }
            if (reduced.Count == 1) return reduced[0];
            var next = string.Join('\n', reduced);
            if (next.Length == 0 || TokenBudget.Count(next, local) >= TokenBudget.Count(text, local)) return null;
            text = next;
        }
        return null;
    }
}
