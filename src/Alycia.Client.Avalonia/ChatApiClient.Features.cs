using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Alycia.Modules.Chat.Application;

namespace Alycia.Client.Avalonia;

public sealed record SettingsReply(ConversationSummary Conversation, ChatOptions Options);
public sealed record ContextPreview(ContextWindow Context, ChatOptions Options, int Revision, int OmittedMessages, string Estimator);
public sealed record PromptItem(Guid Id, string Name, string SystemPrompt)
{
    public override string ToString() => Name;
}
public sealed record ConversationSearch(ConversationSummary[] Items, bool HasMore);

public sealed partial class ChatApiClient
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private HttpRequestMessage CreateRequest(HttpMethod method, string path, object? body)
    {
        var url = _baseUrl ?? Environment.GetEnvironmentVariable("ALYCIA_API_BASE_URL") ?? "http://127.0.0.1:8080";
        if (!Uri.TryCreate(url, UriKind.Absolute, out var origin) || !origin.IsLoopback
            || origin.Scheme is not ("http" or "https") || origin.AbsolutePath != "/"
            || origin.UserInfo.Length > 0 || origin.Query.Length > 0 || origin.Fragment.Length > 0)
            throw new ChatApiException("ALYCIA_API_BASE_URL doit désigner le backend sur cette machine (HTTP ou HTTPS).");
        var request = new HttpRequestMessage(method, new Uri(origin, "/api/chat" + path));
        var token = _accessToken ?? Environment.GetEnvironmentVariable("ALYCIA_CHAT_ACCESS_TOKEN");
        if (!string.IsNullOrEmpty(token)) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null) request.Content = body as HttpContent ?? JsonContent.Create(body);
        return request;
    }

    public Task<SettingsReply> SettingsAsync(Guid id, CancellationToken ct) =>
        SendAsync<SettingsReply>(HttpMethod.Get, $"/conversations/{id}/settings", null, ct);
    public Task<ConversationSummary> ConfigureAsync(Guid id, int revision, string model, string systemPrompt,
        string notes, ChatOptions options, CancellationToken ct) => SendAsync<ConversationSummary>(HttpMethod.Put,
            $"/conversations/{id}/settings", new { revision, model, systemPrompt, contextNotes = notes, options }, ct);
    public Task<ContextPreview> PreviewAsync(Guid id, string content, CancellationToken ct) =>
        SendAsync<ContextPreview>(HttpMethod.Post, $"/conversations/{id}/context", new { content }, ct);
    public Task<FileItem[]> FilesAsync(Guid id, CancellationToken ct) =>
        SendAsync<FileItem[]>(HttpMethod.Get, $"/conversations/{id}/files", null, ct);
    public Task<FileItem> ConfigureFileAsync(Guid id, Guid file, int revision, string mode, CancellationToken ct) =>
        SendAsync<FileItem>(HttpMethod.Patch, $"/conversations/{id}/files/{file}", new { revision, mode }, ct);
    public Task<PromptItem[]> PromptsAsync(CancellationToken ct) => SendAsync<PromptItem[]>(HttpMethod.Get, "/prompts", null, ct);
    public Task<PromptItem> SavePromptAsync(Guid? id, string name, string systemPrompt, CancellationToken ct) =>
        SendAsync<PromptItem>(id.HasValue ? HttpMethod.Put : HttpMethod.Post,
            id.HasValue ? $"/prompts/{id}" : "/prompts", new { name, systemPrompt }, ct);
    public Task DeletePromptAsync(Guid id, CancellationToken ct) => SendAsync<object>(HttpMethod.Delete, $"/prompts/{id}", null, ct);
    public Task<ConversationSearch> SearchAsync(string query, int offset, CancellationToken ct) =>
        SendAsync<ConversationSearch>(HttpMethod.Get, $"/search?q={Uri.EscapeDataString(query)}&offset={offset}", null, ct);
    public Task<byte[]> ExportAsync(Guid id, string format, CancellationToken ct) =>
        SendAsync<byte[]>(HttpMethod.Get, $"/conversations/{id}/export?format={format}", null, ct);
    public Task<ConversationSummary> ImportConversationAsync(byte[] json, CancellationToken ct) =>
        SendAsync<ConversationSummary>(HttpMethod.Post, "/import", new ByteArrayContent(json)
        { Headers = { ContentType = new MediaTypeHeaderValue("application/json") } }, ct);
    public Task<ConversationSummary> ForkAsync(Guid id, int revision, int? through, CancellationToken ct) =>
        SendAsync<ConversationSummary>(HttpMethod.Post, $"/conversations/{id}/fork", new { revision, throughSequence = through }, ct);

    public async Task<ChatSendResult> StreamMessageAsync(Guid id, Guid requestId, string content, int revision,
        Func<ModelDelta, Task> onDelta, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(_timeout);
        using var request = CreateRequest(HttpMethod.Post, $"/conversations/{id}/stream", new { requestId, content, revision });
        try
        {
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (!response.IsSuccessStatusCode)
            {
                ApiError? error = null;
                try { error = await response.Content.ReadFromJsonAsync<ApiError>(timeout.Token); }
                catch (JsonException) { }
                throw new ChatApiException(error?.Message ?? $"Génération refusée (HTTP {(int)response.StatusCode}).");
            }
            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
            var received = 0;
            // The final event contains the persisted answer, so it can exceed a delta line.
            await foreach (var line in BoundedLines.ReadAsync(stream, timeout.Token, 8 * 1024 * 1024))
            {
                timeout.CancelAfter(_timeout);
                if (string.IsNullOrWhiteSpace(line)) continue;
                using var json = JsonDocument.Parse(line);
                var root = json.RootElement;
                switch (root.GetProperty("type").GetString())
                {
                    case "delta":
                        var delta = root.Deserialize<ModelDelta>(Json) ?? throw new ChatApiException("Fragment invalide.");
                        received += delta.Content.Length + (delta.Thinking?.Length ?? 0);
                        if (received > 1_000_000) throw new ChatApiException("Réponse trop volumineuse.");
                        await onDelta(delta);
                        break;
                    case "done":
                        return root.GetProperty("result").Deserialize<ChatSendResult>(Json)
                            ?? throw new ChatApiException("Confirmation invalide.");
                    case "error":
                        throw new ChatApiException(root.GetProperty("message").GetString() ?? "Génération interrompue.");
                    default:
                        throw new ChatApiException("Événement de génération inconnu.");
                }
            }
            throw new ChatApiException("Flux interrompu sans confirmation. Actualisez l’historique avant de réessayer.");
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        { throw new ChatApiException("Le délai de réponse du backend est dépassé."); }
        catch (Exception exception) when (exception is HttpRequestException or IOException)
        { throw new ChatApiException("Connexion interrompue. Actualisez l’historique avant de réessayer."); }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or System.Collections.Generic.KeyNotFoundException)
        { throw new ChatApiException("Flux du backend invalide."); }
    }
}
