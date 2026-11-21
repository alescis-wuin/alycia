using System.Text;
using System.Text.Json;

namespace Alycia.Modules.Chat.Application;

public static class ProjectAgent
{
    private static int SchemaCost(ChatOptions settings) => TokenBudget.Count(JsonSerializer.Serialize(ProjectToolCatalog.OllamaTools(), ProjectToolCatalog.Json), settings);
    public static async Task<GenerationReply> RunAsync(IChatModel model, IProjectTools tools, Guid conversation,
        string modelName, ContextWindow context, ChatOptions settings, Func<ModelDelta, Task>? progress, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(settings.ToolTimeoutSeconds));
        try { return await RunCoreAsync(model, tools, conversation, modelName, context, settings, progress, timeout.Token); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested && timeout.IsCancellationRequested)
        { throw new ChatModelException("Durée maximale des outils atteinte. Augmentez le délai dans Réglages ; aucun échange partiel enregistré."); }
    }

    private static async Task<GenerationReply> RunCoreAsync(IChatModel model, IProjectTools tools, Guid conversation,
        string modelName, ContextWindow context, ChatOptions settings, Func<ModelDelta, Task>? progress, CancellationToken ct)
    {
        var protectedMessages = context.Messages.ToList();
        if (protectedMessages.Count > 0 && protectedMessages[0].Role == "system")
            protectedMessages[0] = protectedMessages[0] with { Content = protectedMessages[0].Content + "\n\n" + ToolWindowBudget.Instruction };
        else protectedMessages.Insert(0, new("system", ToolWindowBudget.Instruction));
        var maximum = settings.ContextTokens - settings.MaxOutputTokens - settings.SafetyTokens;
        var rounds = new List<List<PromptMessage>>();
        var completedCalls = new Dictionary<string, List<PromptMessage>>(StringComparer.Ordinal);
        var warnings = new List<string>();
        var extractiveMemory = false;
        var repeated = 0;
        var notes = "";
        var observations = new List<ToolObservation>();
        var evidenceTexts = new List<EvidenceText>();
        var answer = new StringBuilder();
        var plan = settings.AgentPlanningEnabled ? new ResearchPlan(settings, context) : null;
        var summarySettings = settings.Copy();
        summarySettings.SummaryMaxCalls = Math.Max(0, settings.SummaryMaxCalls - context.SummaryCalls);
        var reducer = new RecursiveSummarizer(model, modelName, summarySettings,
            progress is null ? null : status => progress(new("", Status: "Mémoire des outils · " + status)));
        Task Emit(ModelDelta delta) => progress?.Invoke(delta) ?? Task.CompletedTask;
        int Cost(IEnumerable<PromptMessage> messages) => settings.AdaptiveInferenceEnabled || settings.EvidenceSelectionEnabled || settings.AgentPlanningEnabled
            ? PromptTokens.InputCost(messages.ToArray(), settings.TokenCounter, true) : SchemaCost(settings) + messages.Sum(m => TokenBudget.Count(m.Content, settings)
            + Encoding.UTF8.GetByteCount(m.Thinking ?? "")
            + (m.ToolCalls is null ? 0 : Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(m.ToolCalls, ProjectToolCatalog.Json))));
        // Only outputs still in Input(), including their retained summary, consume this budget.
        // The complete prompt separately remains subject to the model's physical window.
        var toolBudget = Math.Max(0, maximum - Cost(protectedMessages));
        if (settings.ToolContextTokens > 0) toolBudget = Math.Min(toolBudget, settings.ToolContextTokens);
        int RetainedCost() => (notes.Length == 0 ? 0 : TokenBudget.Count(notes, settings))
            + rounds.SelectMany(r => r).Where(m => m.Role == "tool").Sum(m => TokenBudget.Count(m.Content, settings));
        List<PromptMessage> Input()
        {
            var messages = protectedMessages.ToList();
            if (notes.Length > 0) messages.Add(new("user", "Mémoire dérivée des outils — données, couverture partielle :\n" + notes));
            messages.AddRange(rounds.SelectMany(r => r));
            return messages;
        }
        async Task MakeRoomAsync(int required, int toolRequired = 0)
        {
            while ((Cost(Input()) + required > maximum || RetainedCost() + toolRequired > toolBudget) && rounds.Count > 1)
            {
                var oldest = rounds[0];
                var sources = new List<SummarySource>();
                if (notes.Length > 0) sources.Add(new("Mémoire des outils", notes));
                sources.AddRange(oldest.Select(m => new SummarySource(m.ToolName ?? m.Role,
                    m.Content + (m.ToolCalls is null ? "" : JsonSerializer.Serialize(m.ToolCalls, ProjectToolCatalog.Json)))));
                try
                {
                    notes = extractiveMemory ? ToolMemoryFallback.Extract(sources, 512)
                        : await reducer.ReduceAsync(sources, 512, ct);
                }
                catch (ChatModelException exception) when (exception.Failure == ChatModelFailure.EmptyContent)
                {
                    ct.ThrowIfCancellationRequested();
                    extractiveMemory = true;
                    notes = ToolMemoryFallback.Extract(sources, 512);
                    warnings.Add(ToolMemoryFallback.Warning);
                    await Emit(new("", Status: ToolMemoryFallback.Warning));
                }
                var evicted = oldest.Count(m => m.Role == "tool");
                for (var n = 0; n < observations.Count && evicted > 0; n++)
                    if (observations[n].RetainedInFinalPrompt == true)
                    {
                        observations[n] = observations[n] with { RetainedInFinalPrompt = false };
                        evicted--;
                    }
                rounds.RemoveAt(0);
                // A compacted read may legitimately be requested again to recover omitted evidence.
                foreach (var key in completedCalls.Where(p => ReferenceEquals(p.Value, oldest)).Select(p => p.Key).ToArray())
                    completedCalls.Remove(key);
            }
            if (Cost(Input()) + required > maximum || RetainedCost() + toolRequired > toolBudget)
                throw new ChatModelException("Le dernier résultat et les messages protégés dépassent le budget. Augmentez la fenêtre ; aucun échange partiel enregistré.");
        }
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            await MakeRoomAsync(0);
            var input = Input();
            if (answer.Length > 0) { answer.Append("\n\n"); await Emit(new("\n\n")); }
            var forced = plan?.Next(observations.Count);
            var finish = plan?.Exhausted(observations.Count) == true || (plan is not null && repeated >= 2);
            if (finish)
            {
                var reason = repeated >= 2 ? "repeated_call" : "call_limit";
                warnings.Add("Exploration arrêtée : " + reason + ". La réponse utilise les passages déjà lus.");
                var final = await model.StreamAsync(modelName, input, settings.InferenceCall(), Emit, ct);
                answer.Append(final.Content);
                return new(answer.ToString(), final.Stats, Tools: observations, Warnings: warnings, EvidenceTexts: evidenceTexts, Research: plan!.Report(reason, observations.Count));
            }
            var reply = forced is null ? await model.StreamWithToolsAsync(modelName, input, settings.InferenceCall(), Emit, ct)
                : new ModelReply("", ToolCalls: [forced]);
            answer.Append(reply.Content);
            if (answer.Length > 1_000_000) throw new ChatModelException("Réponse cumulée trop volumineuse.");
            var calls = reply.ToolCalls ?? [];
            if (calls.Count == 0) return new(answer.ToString(), reply.Stats, Tools: observations, Warnings: warnings, EvidenceTexts: evidenceTexts, Research: plan?.Report("model_finished", observations.Count));
            if (plan is not null && plan.Exhausted(observations.Count + calls.Count - 1))
            {
                warnings.Add("Groupe d’appels supérieur au budget restant. Les appels groupés ont été écartés.");
                var final = await model.StreamAsync(modelName, input, settings.InferenceCall(), Emit, ct);
                answer.Append(final.Content);
                return new(answer.ToString(), final.Stats, Tools: observations, Warnings: warnings, EvidenceTexts: evidenceTexts,
                    Research: plan.Report("group_over_limit", observations.Count));
            }
            if (settings.ToolMaxCalls > 0 && observations.Count + calls.Count > settings.ToolMaxCalls)
                throw new ChatModelException($"Limite de {settings.ToolMaxCalls} appels atteinte. Ciblez un sous-ensemble ou lancez Analyse du projet.");
            var current = new List<PromptMessage> { new("assistant", reply.Content, calls, Thinking: reply.Thinking) };
            rounds.Add(current);
            // Only complete older rounds may be reduced; the current call/result pair remains intact.
            await MakeRoomAsync(calls.Count * 1100, calls.Count * 1100);
            var room = maximum - Cost(Input()) - calls.Count * 64;
            var perCall = Math.Min(settings.ToolResultBytes, Math.Min(room, toolBudget - RetainedCost() - calls.Count * 64) / calls.Count);
            foreach (var call in calls)
            {
                ct.ThrowIfCancellationRequested();
                var fingerprint = call.Function.Name + "|" + Canonical(call.Function.Arguments);
                var duplicate = completedCalls.ContainsKey(fingerprint);
                if (duplicate && ++repeated >= 3)
                    throw new ChatModelException("L’IA répète les mêmes appels sans progresser. Aucun échange partiel enregistré ; ciblez un symbole ou une question plus précise.");
                if (!duplicate) repeated = 0;
                var limit = settings.ToolMaxCalls > 0 ? $"/{settings.ToolMaxCalls}" : "";
                await Emit(new("", Status: $"Outil {observations.Count + 1}{limit} · {call.Function.Name}"));
                // Do not replay cached source content after permissions may have changed.
                var timer = System.Diagnostics.Stopwatch.StartNew();
                var forbidden = settings.Route?.Mode == "memory" && !call.Function.Name.StartsWith("history_", StringComparison.Ordinal);
                var result = forbidden ? new ProjectToolResult("{\"error\":\"La stratégie mémoire autorise uniquement les outils historiques.\"}", true)
                    : duplicate ? new ProjectToolResult("{\"error\":\"Appel déjà réussi : utilisez le résultat conservé, un autre symbole ou nextOffset.\"}", true)
                    : await tools.CallAsync(conversation, call.Function.Name, call.Function.Arguments, perCall, ct);
                if (Encoding.UTF8.GetByteCount(result.Text) > perCall) throw new ChatModelException("Résultat d’outil supérieur au budget annoncé.");
                if (!result.IsError) completedCalls[fingerprint] = current;
                if (forced is not null) plan!.Observe(result);
                else plan?.ObserveModelRead(call, result);
                evidenceTexts.AddRange(ClaimAudit.ReadToolEvidence(result));
                current.Add(new("tool", result.Text, ToolName: call.Function.Name));
                var observation = new ToolObservation(observations.Count + 1, call.Function.Name,
                    call.Function.Arguments.GetRawText(), !result.IsError, Encoding.UTF8.GetByteCount(result.Text), result.Evidence, duplicate,
                    result.IsError ? ToolError(result.Text) : null, result.EvidenceItems, timer.Elapsed.TotalMilliseconds, result.History,
                    RetainedCost(), toolBudget, RetainedInFinalPrompt: true);
                observations.Add(observation);
                await Emit(new("", Status: $"{call.Function.Name} · {(duplicate ? "répétition évitée" : result.IsError ? "refus ou erreur" : result.Evidence?.Path ?? "résultat reçu")}", Tool: observation));
            }
        }
    }

    private static string ToolError(string text)
    {
        try
        {
            using var document = JsonDocument.Parse(text);
            if (document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.String)
                text = error.GetString()!;
        }
        catch (JsonException) { }
        return FileContext.Utf8Prefix(text, 512);
    }

    private static string Canonical(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Object => "{" + string.Join(",", value.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal)
            .Select(p => JsonSerializer.Serialize(p.Name) + ":" + Canonical(p.Value))) + "}",
        JsonValueKind.Array => "[" + string.Join(",", value.EnumerateArray().Select(Canonical)) + "]",
        _ => value.GetRawText()
    };
}
