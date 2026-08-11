using System.Net;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Alicia.Application.Conversations;
using Alicia.Domain.Conversations;

namespace Alicia.Infrastructure.Providers.LlamaCpp;

internal sealed class LlamaCppChatClient
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly HttpClient _httpClient;

    public LlamaCppChatClient(HttpClient httpClient)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        _httpClient = httpClient;
    }

    public async IAsyncEnumerable<ConversationResponseChunk> StreamAsync(
        Uri endpoint,
        ConversationResponseRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentNullException.ThrowIfNull(request);

        Uri requestUri = new(endpoint, "v1/chat/completions");
        string payload = JsonSerializer.Serialize(
            new ChatCompletionRequest(
                LlamaCppServerCommand.ModelAlias,
                request.Messages.Select(MapMessage).ToArray(),
                Stream: true),
            _jsonOptions);
        using HttpRequestMessage httpRequest = new(HttpMethod.Post, requestUri)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json"),
        };
        using HttpResponseMessage response = await _httpClient
            .SendAsync(
                httpRequest,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            string responseBody = await response.Content
                .ReadAsStringAsync(cancellationToken)
                .ConfigureAwait(false);
            throw CreateHttpFailure(response.StatusCode, responseBody);
        }

        await using Stream responseStream = await response.Content
            .ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);
        using StreamReader reader = new(responseStream, Encoding.UTF8);

        while (true)
        {
            string? line = await reader.ReadLineAsync(cancellationToken)
                .ConfigureAwait(false);

            if (line is null)
            {
                throw new InvalidDataException(
                    "llama.cpp streaming response ended before the [DONE] marker.");
            }

            if (!line.StartsWith("data:", StringComparison.Ordinal))
            {
                continue;
            }

            string data = line["data:".Length..].TrimStart();

            if (string.Equals(data, "[DONE]", StringComparison.Ordinal))
            {
                yield break;
            }

            if (string.IsNullOrWhiteSpace(data))
            {
                continue;
            }

            string? contentDelta = ParseContentDelta(data);

            if (!string.IsNullOrEmpty(contentDelta))
            {
                yield return new ConversationResponseChunk(contentDelta);
            }
        }
    }

    internal static string? ParseContentDelta(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        JsonDocument document;

        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                "llama.cpp returned malformed JSON while streaming a response.",
                exception);
        }

        using (document)
        {
            JsonElement root = document.RootElement;

            if (!root.TryGetProperty("choices", out JsonElement choices)
                || choices.ValueKind != JsonValueKind.Array
                || choices.GetArrayLength() == 0)
            {
                return null;
            }

            JsonElement firstChoice = choices[0];

            if (!firstChoice.TryGetProperty("delta", out JsonElement delta)
                || !delta.TryGetProperty("content", out JsonElement content)
                || content.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            return content.GetString();
        }
    }

    private static ChatCompletionMessage MapMessage(ChatMessage message)
    {
        string role = message.Role switch
        {
            MessageRole.System => "system",
            MessageRole.User => "user",
            MessageRole.Assistant => "assistant",
            _ => throw new InvalidOperationException(
                $"Unsupported conversation role '{message.Role}'."),
        };

        return new ChatCompletionMessage(role, message.Content);
    }

    private static InvalidOperationException CreateHttpFailure(
        HttpStatusCode statusCode,
        string responseBody)
    {
        string detail = string.IsNullOrWhiteSpace(responseBody)
            ? "No response body was returned."
            : responseBody.Trim();

        if (detail.Length > 600)
        {
            detail = $"{detail[..600]}…";
        }

        return new InvalidOperationException(
            $"llama.cpp returned HTTP {(int)statusCode} ({statusCode}). {detail}");
    }

    private sealed record ChatCompletionRequest(
        string Model,
        IReadOnlyList<ChatCompletionMessage> Messages,
        bool Stream);

    private sealed record ChatCompletionMessage(
        string Role,
        string Content);
}
