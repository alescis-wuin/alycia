using System;
using System.Net;
using System.IO;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Alycia.Client.Avalonia;

public sealed record ConversationSummary(Guid Id, string Title, string Model, string SystemPrompt,
    DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc, int Revision, int MessageCount);

public sealed record ChatMessageItem(Guid ConversationId, int Sequence, Guid RequestId, string Role,
    string Content, DateTimeOffset CreatedAtUtc, Guid? AttachmentId = null)
{
    public bool IsFile => AttachmentId.HasValue;
    public bool IsUser => Role == "user";
    public string Label => $"{(IsFile ? "Fichier joint" : Role == "user" ? "Vous" : "Alycia")} · {CreatedAtUtc.ToLocalTime():HH:mm}";
}

public sealed record MessagePage(ConversationSummary Conversation, ChatMessageItem[] Messages, bool HasMore, FileItem[]? Files = null);
public sealed record ChatSendResult(ChatMessageItem[] Messages, int? HistoryMessagesIncluded, bool Replayed, int FilesIncluded = 0);
public sealed record FileItem(Guid Id, string Name, long Size, string Status, int AnchorSequence,
    DateTimeOffset CreatedAtUtc, Guid? ArchiveId, string Sha256)
{
    public ChatMessageItem ToMessage(Guid conversation) => new(conversation, AnchorSequence, Guid.Empty, "file",
        $"{Name}\n{Size / 1024.0:N1} Kio · {Status}", CreatedAtUtc, Id);
}
public sealed record UploadFile(string Name, Stream Content);

public sealed class ChatApiException(string message) : Exception(message);

public sealed class ChatApiClient
{
    private static readonly HttpClient SharedHttp = new(new HttpClientHandler { AllowAutoRedirect = false })
    {
        Timeout = Timeout.InfiniteTimeSpan
    };
    private readonly HttpClient _http;
    private readonly string? _baseUrl;
    private readonly TimeSpan _timeout;
    private readonly string? _accessToken;

    public ChatApiClient() : this(SharedHttp, null, TimeSpan.FromMinutes(11)) { }

    public ChatApiClient(HttpClient http, string? baseUrl, TimeSpan timeout, string? accessToken = null)
    {
        _http = http;
        _baseUrl = baseUrl;
        _timeout = timeout;
        _accessToken = accessToken;
    }

    public Task<string[]> ModelsAsync(CancellationToken ct) =>
        SendAsync<string[]>(HttpMethod.Get, "/models", null, ct);

    public Task<ConversationSummary[]> ListAsync(CancellationToken ct) =>
        SendAsync<ConversationSummary[]>(HttpMethod.Get, "/conversations", null, ct);

    public Task<ConversationSummary> CreateAsync(string title, string model, string? systemPrompt, CancellationToken ct) =>
        SendAsync<ConversationSummary>(HttpMethod.Post, "/conversations", new { title, model, systemPrompt }, ct);

    public Task<MessagePage> MessagesAsync(Guid id, int? before, CancellationToken ct) =>
        SendAsync<MessagePage>(HttpMethod.Get, $"/conversations/{id}/messages"
            + (before is null ? "" : $"?before={before}"), null, ct);

    public Task<ConversationSummary> RenameAsync(Guid id, string title, CancellationToken ct) =>
        SendAsync<ConversationSummary>(HttpMethod.Patch, $"/conversations/{id}", new { title }, ct);

    public async Task DeleteAsync(Guid id, CancellationToken ct) =>
        await SendAsync<object>(HttpMethod.Delete, $"/conversations/{id}", null, ct);

    public Task<ChatSendResult> SendMessageAsync(Guid id, Guid requestId, string content, CancellationToken ct) =>
        SendAsync<ChatSendResult>(HttpMethod.Post, $"/conversations/{id}/messages", new { requestId, content }, ct);

    public async Task<FileItem[]> UploadAsync(Guid conversation, Guid batch, IReadOnlyList<UploadFile> files, CancellationToken ct)
    {
        using var form = new MultipartFormDataContent();
        foreach (var file in files) form.Add(new StreamContent(file.Content), "files", file.Name);
        return await SendAsync<FileItem[]>(HttpMethod.Post, $"/conversations/{conversation}/files/{batch}", form, ct);
    }

    public Task DeleteFileAsync(Guid conversation, Guid file, CancellationToken ct) =>
        SendAsync<object>(HttpMethod.Delete, $"/conversations/{conversation}/files/{file}", null, ct);

    public async Task DownloadAsync(Guid conversation, Guid file, Stream destination, CancellationToken ct)
    {
        var bytes = await SendAsync<byte[]>(HttpMethod.Get, $"/conversations/{conversation}/files/{file}", null, ct);
        await destination.WriteAsync(bytes, ct);
    }

    private async Task<T> SendAsync<T>(HttpMethod method, string path, object? body, CancellationToken ct)
    {
        var url = _baseUrl ?? Environment.GetEnvironmentVariable("ALYCIA_API_BASE_URL") ?? "http://127.0.0.1:8080";
        if (!Uri.TryCreate(url, UriKind.Absolute, out var origin) || !origin.IsLoopback
            || origin.Scheme is not ("http" or "https") || origin.AbsolutePath != "/"
            || origin.UserInfo.Length > 0 || origin.Query.Length > 0 || origin.Fragment.Length > 0)
        {
            throw new ChatApiException("ALYCIA_API_BASE_URL doit désigner le backend sur cette machine (HTTP ou HTTPS). ");
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        // Short requests fail quickly; inference may need time to load a model.
        timeout.CancelAfter(method == HttpMethod.Post && (path.EndsWith("/messages", StringComparison.Ordinal) || body is MultipartFormDataContent)
            ? _timeout : TimeSpan.FromSeconds(30));
        using var request = new HttpRequestMessage(method, new Uri(origin, "/api/chat" + path));
        var token = _accessToken ?? Environment.GetEnvironmentVariable("ALYCIA_CHAT_ACCESS_TOKEN");
        if (!string.IsNullOrEmpty(token)) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null)
        {
            request.Content = body as HttpContent ?? JsonContent.Create(body);
        }

        try
        {
            using var response = await _http.SendAsync(request, timeout.Token);
            if (!response.IsSuccessStatusCode)
            {
                ApiError? error = null;
                try
                {
                    error = await response.Content.ReadFromJsonAsync<ApiError>(timeout.Token);
                }
                catch (JsonException) { }

                throw new ChatApiException(error?.Message ?? (response.StatusCode == HttpStatusCode.NotFound
                    ? "Le chat est désactivé. Lancez le backend en Development avec ALYCIA_ENABLE_LOCAL_CHAT=true."
                    : $"Le backend a refusé la demande (HTTP {(int)response.StatusCode})."));
            }

            if (response.StatusCode == HttpStatusCode.NoContent)
            {
                return default!;
            }

            if (typeof(T) == typeof(byte[])) return (T)(object)await response.Content.ReadAsByteArrayAsync(timeout.Token);

            return await response.Content.ReadFromJsonAsync<T>(timeout.Token)
                ?? throw new ChatApiException("Réponse du backend vide.");
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new ChatApiException("Le backend ne répond pas dans le délai prévu. Actualisez avant de réessayer.");
        }
        catch (HttpRequestException)
        {
            throw new ChatApiException("Backend inaccessible. Vérifiez son démarrage et ALYCIA_API_BASE_URL.");
        }
        catch (JsonException)
        {
            throw new ChatApiException("Réponse du backend invalide.");
        }
    }

    private sealed record ApiError(string? Message);
}
