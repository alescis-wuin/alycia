using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Alycia.Modules.Chat.Application;

namespace Alycia.Modules.Chat.Infrastructure;

public sealed class OllamaChatModel(HttpClient http, ChatOptions options) : IChatModel
{
    public async Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        try
        {
            using var response = await http.GetAsync("api/tags", timeout.Token);
            response.EnsureSuccessStatusCode();
            var payload = await response.Content.ReadFromJsonAsync<ModelList>(timeout.Token);
            if (payload?.Models is null)
            {
                throw new ChatModelException("La liste des modèles Ollama est invalide.");
            }

            return payload.Models.Where(item => !string.IsNullOrWhiteSpace(item.Name)
                    && !item.Name.Contains(":cloud", StringComparison.OrdinalIgnoreCase)
                    && string.IsNullOrEmpty(item.RemoteModel) && string.IsNullOrEmpty(item.RemoteHost))
                .Select(item => item.Name!).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
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

    public async Task<string> GenerateAsync(string model, IReadOnlyList<PromptMessage> messages,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(options.TimeoutSeconds));
        try
        {
            // A model may have been replaced by a cloud alias since creation.
            if (!(await ListModelsAsync(timeout.Token)).Contains(model, StringComparer.Ordinal))
            {
                throw new ChatModelException("Ce modèle local n’est plus disponible dans Ollama.");
            }

            using var response = await http.PostAsJsonAsync("api/chat", new
            {
                model,
                messages,
                stream = false,
                options = new { num_ctx = options.ContextTokens, num_predict = options.MaxOutputTokens }
            }, timeout.Token);
            if (!response.IsSuccessStatusCode)
            {
                throw new ChatModelException($"Ollama a refusé la génération (HTTP {(int)response.StatusCode}). Vérifiez le modèle et la mémoire disponible.");
            }

            var payload = await response.Content.ReadFromJsonAsync<ChatReply>(timeout.Token);
            if (payload is not { Done: true, Message.Role: "assistant" }
                || string.IsNullOrWhiteSpace(payload.Message.Content))
            {
                throw new ChatModelException("Ollama a renvoyé une réponse vide ou incomplète.");
            }

            return payload.Message.Content;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new ChatModelException("Le délai de génération Ollama est dépassé. Essayez un modèle plus petit ou augmentez Chat__TimeoutSeconds.");
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

    private sealed record ModelInfo(string? Name,
        [property: JsonPropertyName("remote_model")] string? RemoteModel,
        [property: JsonPropertyName("remote_host")] string? RemoteHost);
    private sealed record ModelList(ModelInfo[]? Models);
    private sealed record ChatReply(bool Done, PromptMessage? Message);
}
