using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Alycia.Modules.Inference;

public sealed partial class OllamaTextGeneration(HttpClient http, InferenceScheduler scheduler, ModelTokenizationCache tokenizers) : ITextGeneration
{
    public async Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        try
        {
            var names = await LocalModelNamesAsync(timeout.Token);
            var available = new ConcurrentBag<string>();
            // /api/tags includes embeddings. /api/show reports capabilities without generating text.
            await Parallel.ForEachAsync(names, new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = timeout.Token },
                async (name, ct) =>
                {
                    var details = await ModelDetailsAsync(name, ct);
                    if (details is not null && details.SupportsChat) available.Add(name);
                });
            return available.Order(StringComparer.Ordinal).ToArray();
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new ChatModelException("Ollama ne répond pas. Vérifiez qu’il est démarré.");
        }
        catch (HttpRequestException)
        {
            throw new ChatModelException("Ollama est inaccessible. Démarrez ollama serve sur cette machine.");
        }
        catch (JsonException)
        {
            throw new ChatModelException("Réponse Ollama invalide.");
        }
    }

    private async Task<string[]> LocalModelNamesAsync(CancellationToken ct)
    {
        using var response = await http.GetAsync("api/tags", ct);
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<ModelList>(ct);
        if (payload?.Models is null) throw new ChatModelException("La liste des modèles Ollama est invalide.");
        return payload.Models.Where(item => !string.IsNullOrWhiteSpace(item.Name)
                && !item.Name.Contains(":cloud", StringComparison.OrdinalIgnoreCase)
                && string.IsNullOrEmpty(item.RemoteModel) && string.IsNullOrEmpty(item.RemoteHost))
            .Select(item => item.Name!).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
    }

    private async Task<ModelDetails?> ModelDetailsAsync(string model, CancellationToken ct)
    {
        using var response = await http.PostAsJsonAsync("api/show", new { model, verbose = false }, ct);
        if (response.StatusCode == HttpStatusCode.NotFound) return null; // Removed since /api/tags.
        if (!response.IsSuccessStatusCode)
            throw new ChatModelException($"Impossible de vérifier les capacités du modèle « {model} » (HTTP {(int)response.StatusCode}). Vérifiez la version d’Ollama.");
        var details = await response.Content.ReadFromJsonAsync<ModelDetails>(ct);
        if (details?.Capabilities is null)
            throw new ChatModelException($"Ollama ne fournit pas les capacités du modèle « {model} ». Actualisez Ollama puis la liste des modèles.");
        return details;
    }

    private async Task<ModelDetails> ValidateChatModelAsync(string model, CancellationToken ct)
    {
        // Recheck locality and only this model's capabilities before each generation.
        if (!(await LocalModelNamesAsync(ct)).Contains(model, StringComparer.Ordinal))
            throw new ChatModelException($"Le modèle local « {model} » n’est plus disponible. Choisissez un modèle dans Réglages.");
        var details = await ModelDetailsAsync(model, ct);
        if (details is null)
            throw new ChatModelException($"Le modèle « {model} » a été retiré d’Ollama. Choisissez un autre modèle dans Réglages.");
        if (!details.SupportsChat)
            throw new ChatModelException(details.Capabilities!.Contains("embedding", StringComparer.Ordinal)
                ? $"Le modèle « {model} » produit des embeddings et ne peut pas répondre à une conversation. Choisissez un modèle génératif dans Réglages."
                : $"Le modèle « {model} » ne propose pas de génération textuelle locale. Choisissez un autre modèle dans Réglages.");
        return details;
    }

    public async Task<bool> SupportsToolsAsync(string model, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        try { return (await ValidateChatModelAsync(model, timeout.Token)).Capabilities!.Contains("tools", StringComparer.Ordinal); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new ChatModelException("Vérification des outils Ollama expirée."); }
        catch (HttpRequestException) { throw new ChatModelException("Ollama est inaccessible."); }
        catch (JsonException) { throw new ChatModelException("Capacités Ollama invalides."); }
    }

    public async Task<ModelReply> GenerateAsync(string model, IReadOnlyList<PromptMessage> messages,
        GenerationParameters parameters, CancellationToken cancellationToken)
    {
        try
        {
            using var inference = await scheduler.AcquireAsync(TimeSpan.FromSeconds(parameters.TimeoutSeconds),
                cancellationToken, annotation: parameters.CallPurpose == "annotation");
            await ValidateChatModelAsync(model, inference.Token);

            var request = new Dictionary<string, object>
            {
                ["model"] = model,
                ["messages"] = messages,
                ["stream"] = false,
                ["options"] = GenerationOptions(parameters)
            };
            // Auxiliary tasks have a bounded output budget; do not spend it on optional reasoning.
            // This does not change the reasoning setting of conversational/tool turns.
            if (parameters.CallPurpose is "summary" or "memory" or "annotation") request["think"] = false;
            if (parameters.ResponseSchema is { } schema) request["format"] = schema;
            using var response = await http.PostAsJsonAsync("api/chat", request, inference.Token);
            if (!response.IsSuccessStatusCode)
            {
                throw new ChatModelException($"Ollama a refusé la génération avec « {model} » (HTTP {(int)response.StatusCode}). Vérifiez les réglages de cette conversation et le journal Ollama.");
            }

            var payload = await response.Content.ReadFromJsonAsync<ChatReply>(inference.Token);
            var stage = parameters.CallPurpose switch { "summary" => "le résumé interne", "memory" => "l’extraction de mémoire", _ => "la génération" };
            if (payload is not { Done: true, Message.Role: "assistant" })
            {
                throw new ChatModelException($"Ollama a renvoyé une réponse incomplète pendant {stage} : fin non confirmée ou rôle invalide.");
            }
            var stats = new GenerationStats(payload.PromptTokens, payload.OutputTokens, payload.TotalDuration,
                payload.EvaluationDuration, payload.DoneReason, payload.PromptDuration, payload.LoadDuration, payload.CachedTokens);
            if (string.IsNullOrWhiteSpace(payload.Message.Content))
            {
                var reason = payload.DoneReason == "length" ? "Limite de génération atteinte." : "Aucun texte final reçu.";
                var thinking = string.IsNullOrWhiteSpace(payload.Message.Thinking) ? "" : " Le modèle a produit du raisonnement sans réponse finale.";
                throw new ChatModelException($"Ollama a renvoyé un contenu vide pendant {stage}. {reason}{thinking}", stats, ChatModelFailure.EmptyContent);
            }
            ValidateCompletion(payload.DoneReason, parameters);
            return new(payload.Message.Content, stats);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new ChatModelException(parameters.CallPurpose == "annotation"
                ? $"Synthèse d’indexation interrompue après {parameters.TimeoutSeconds} s. Augmentez le délai d’enrichissement dans les réglages d’indexation ou choisissez un modèle plus petit."
                : $"Le délai de génération Ollama ({parameters.TimeoutSeconds} s) est dépassé. Augmentez le délai dans les réglages de la conversation ou choisissez un modèle plus petit.",
                failure: ChatModelFailure.Timeout);
        }
        catch (HttpRequestException)
        {
            throw new ChatModelException("La connexion avec Ollama a été interrompue.");
        }
        catch (JsonException)
        {
            throw new ChatModelException("Réponse Ollama invalide.");
        }
    }

    private static Dictionary<string, object> GenerationOptions(IGenerationSettings settings)
    {
        var values = new Dictionary<string, object>
        {
            ["num_ctx"] = settings.ContextTokens,
            ["num_predict"] = settings.GenerateUntilStop ? -1 : settings.MaxOutputTokens,
            ["temperature"] = settings.Temperature
        };
        if (settings.Seed.HasValue) values["seed"] = settings.Seed.Value;
        return values;
    }

    private sealed record ModelInfo(string? Name,
        [property: JsonPropertyName("remote_model")] string? RemoteModel,
        [property: JsonPropertyName("remote_host")] string? RemoteHost);
    private sealed record ModelList(ModelInfo[]? Models);
    private sealed record ModelDetails(string[]? Capabilities,
        [property: JsonPropertyName("remote_model")] string? RemoteModel,
        [property: JsonPropertyName("remote_host")] string? RemoteHost)
    {
        public bool SupportsChat => Capabilities?.Contains("completion", StringComparer.Ordinal) == true
            && string.IsNullOrEmpty(RemoteModel) && string.IsNullOrEmpty(RemoteHost);
    }
    private static void ValidateCompletion(string? reason, IGenerationSettings settings)
    {
        if (settings.GenerateUntilStop && reason != "stop")
            throw new ChatModelException(reason == "length"
                ? "Ollama a atteint une limite avant son signal d’arrêt. La réponse partielle n’est pas enregistrée."
                : "Ollama n’a pas confirmé une fin normale (done_reason=stop). Aucun échange enregistré.");
    }

    private sealed record ChatReply(bool Done, PromptMessage? Message,
        [property: JsonPropertyName("done_reason")] string? DoneReason,
        [property: JsonPropertyName("prompt_eval_count")] int? PromptTokens,
        [property: JsonPropertyName("eval_count")] int? OutputTokens,
        [property: JsonPropertyName("total_duration")] long? TotalDuration,
        [property: JsonPropertyName("eval_duration")] long? EvaluationDuration,
        [property: JsonPropertyName("prompt_eval_duration")] long? PromptDuration,
        [property: JsonPropertyName("load_duration")] long? LoadDuration,
        [property: JsonPropertyName("prompt_eval_cached_count")] int? CachedTokens);
}
