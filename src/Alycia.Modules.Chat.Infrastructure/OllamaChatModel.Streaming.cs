using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Alycia.Modules.Chat.Application;

namespace Alycia.Modules.Chat.Infrastructure;

public sealed partial class OllamaChatModel
{
    public const int MaxResponseCharacters = 1_000_000;

    public async Task<GenerationReply> StreamAsync(string model, IReadOnlyList<PromptMessage> messages,
        ChatOptions settings, Func<ModelDelta, Task> onDelta, CancellationToken cancellationToken)
    {
        settings.Validate();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(settings.TimeoutSeconds));
        try
        {
            if (!(await ListModelsAsync(timeout.Token)).Contains(model, StringComparer.Ordinal))
                throw new ChatModelException("Ce modèle local n’est plus disponible dans Ollama.");
            using var request = new HttpRequestMessage(HttpMethod.Post, "api/chat")
            {
                Content = JsonContent.Create(new
                {
                    model, messages, stream = true,
                    options = GenerationOptions(settings)
                })
            };
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (!response.IsSuccessStatusCode)
                throw new ChatModelException($"Ollama a refusé la génération (HTTP {(int)response.StatusCode}).");
            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
            var content = new StringBuilder();
            var characters = 0;
            await foreach (var line in BoundedLines.ReadAsync(stream, timeout.Token))
            {
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
                }
                characters += text.Length + (thinking?.Length ?? 0);
                if (characters > MaxResponseCharacters || text.Contains('\0'))
                    throw new ChatModelException("Réponse Ollama trop volumineuse ou invalide.");
                content.Append(text);
                if (text.Length > 0 || !string.IsNullOrEmpty(thinking)) await onDelta(new(text, thinking));
                if (root.TryGetProperty("done", out var done) && done.GetBoolean())
                {
                    if (string.IsNullOrWhiteSpace(content.ToString()))
                        throw new ChatModelException("Ollama a terminé sans réponse. Augmentez la réserve de sortie si le modèle raisonne.");
                    return new(content.ToString(), new(Number(root, "prompt_eval_count"), Number(root, "eval_count"),
                        Long(root, "total_duration"), Long(root, "eval_duration"),
                        root.TryGetProperty("done_reason", out var reason) ? reason.GetString() : null));
                }
            }
            throw new ChatModelException("Flux Ollama incomplet : aucun échange enregistré.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new ChatModelException("Le délai de génération Ollama est dépassé.");
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
