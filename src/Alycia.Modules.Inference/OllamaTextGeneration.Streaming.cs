using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace Alycia.Modules.Inference;

public sealed partial class OllamaTextGeneration
{
    public const int MaxResponseCharacters = 1_000_000;

    public async Task<ModelReply> StreamAsync(string model, IReadOnlyList<PromptMessage> messages,
        GenerationParameters parameters, Func<ModelDelta, Task> onDelta, bool useTools, CancellationToken cancellationToken)
    {
        try
        {
            using var inference = await scheduler.AcquireAsync(TimeSpan.FromSeconds(parameters.TimeoutSeconds),
                cancellationToken, annotation: parameters.CallPurpose == "annotation");
            var details = await ValidateChatModelAsync(model, inference.Token);
            if (useTools && !details.Capabilities!.Contains("tools", StringComparer.Ordinal))
                throw new ChatModelException("Ce modèle n’annonce pas la capacité tools. Choisissez un modèle compatible ou désactivez les outils.");
            var payload = new Dictionary<string, object>
            { ["model"] = model, ["messages"] = messages, ["stream"] = true, ["options"] = GenerationOptions(parameters) };
            if (useTools) payload["tools"] = ProjectToolCatalog.OllamaTools();
            using var request = new HttpRequestMessage(HttpMethod.Post, "api/chat") { Content = JsonContent.Create(payload) };
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, inference.Token);
            if (!response.IsSuccessStatusCode)
                throw new ChatModelException($"Ollama a refusé la génération avec « {model} » (HTTP {(int)response.StatusCode}). Vérifiez les réglages de cette conversation et le journal Ollama.");
            await using var stream = await response.Content.ReadAsStreamAsync(inference.Token);
            var content = new StringBuilder();
            var thoughts = new StringBuilder();
            var calls = new List<ModelToolCall>();
            var characters = 0;
            await foreach (var line in BoundedLines.ReadAsync(stream, inference.Token))
            {
                inference.RenewDeadline();
                if (string.IsNullOrWhiteSpace(line)) continue;
                using var json = JsonDocument.Parse(line);
                var root = json.RootElement;
                if (root.TryGetProperty("error", out _)) throw new ChatModelException("Ollama a interrompu la génération.");
                var text = "";
                string? thinking = null;
                if (root.TryGetProperty("message", out var message))
                {
                    if (message.TryGetProperty("role", out var role) && role.GetString() != "assistant")
                        throw new ChatModelException("Rôle de réponse Ollama invalide.");
                    text = message.TryGetProperty("content", out var value) ? value.GetString() ?? "" : "";
                    thinking = message.TryGetProperty("thinking", out var thought) ? thought.GetString() : null;
                    if (message.TryGetProperty("tool_calls", out var requested) && requested.ValueKind != JsonValueKind.Null)
                    {
                        if (requested.ValueKind != JsonValueKind.Array) throw new ChatModelException("Appels d’outils invalides.");
                        foreach (var call in requested.EnumerateArray())
                        {
                            if (!useTools || calls.Count >= 8 || call.GetRawText().Length > 4096)
                                throw new ChatModelException("Appels non autorisés ou trop volumineux.");
                            var parsed = call.Deserialize<ModelToolCall>(ProjectToolCatalog.Json);
                            if (parsed?.Function is not { Name.Length: > 0 and <= 100 } function || function.Arguments.ValueKind != JsonValueKind.Object)
                                throw new ChatModelException("Arguments d’outil invalides.");
                            calls.Add(parsed);
                        }
                    }
                }
                characters += text.Length + (thinking?.Length ?? 0);
                if (characters > MaxResponseCharacters || text.Contains('\0'))
                    throw new ChatModelException("Réponse Ollama trop volumineuse ou invalide.");
                content.Append(text);
                if (useTools) thoughts.Append(thinking);
                if (text.Length > 0 || !string.IsNullOrEmpty(thinking)) await onDelta(new(text, thinking));
                if (root.TryGetProperty("done", out var done) && done.GetBoolean())
                {
                    if (string.IsNullOrWhiteSpace(content.ToString()) && calls.Count == 0)
                        throw new ChatModelException("Ollama a terminé sans réponse. Augmentez la réserve de sortie si le modèle raisonne.");
                    ValidateCompletion(root.TryGetProperty("done_reason", out var finish) ? finish.GetString() : null, parameters);
                    return new(content.ToString(), new(Number(root, "prompt_eval_count"), Number(root, "eval_count"),
                        Long(root, "total_duration"), Long(root, "eval_duration"),
                        root.TryGetProperty("done_reason", out var reason) ? reason.GetString() : null,
                        Long(root, "prompt_eval_duration"), Long(root, "load_duration"), Number(root, "prompt_eval_cached_count")),
                        calls.Count == 0 ? null : calls, useTools ? thoughts.ToString() : null);
                }
            }
            throw new ChatModelException("Flux Ollama incomplet : aucun échange enregistré.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new ChatModelException("Ollama est resté inactif trop longtemps. La génération a été interrompue.", failure: ChatModelFailure.Timeout);
        }
        catch (HttpRequestException) { throw new ChatModelException("La connexion avec Ollama a été interrompue."); }
        catch (IOException) { throw new ChatModelException("Le flux Ollama a été interrompu."); }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or FormatException)
        {
            throw new ChatModelException("Flux Ollama invalide.");
        }
    }

    private static int? Number(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.TryGetInt32(out var number) ? number : null;
    private static long? Long(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.TryGetInt64(out var number) ? number : null;
}
