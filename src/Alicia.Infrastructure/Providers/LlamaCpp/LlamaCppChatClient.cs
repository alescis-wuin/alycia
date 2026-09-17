using System.Net;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Alicia.Application.Conversations;
using Alicia.Application.Providers;
using Alicia.Domain.Conversations;

namespace Alicia.Infrastructure.Providers.LlamaCpp;

internal sealed class LlamaCppChatClient
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly HttpClient _httpClient;
    private readonly LlamaCppProviderTimeouts _timeouts;

    public LlamaCppChatClient(
        HttpClient httpClient,
        LlamaCppProviderTimeouts? timeouts = null)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        _httpClient = httpClient;
        _timeouts = timeouts ?? LlamaCppProviderTimeouts.Default;
    }

    public IAsyncEnumerable<ConversationResponseChunk> StreamAsync(
        Uri endpoint,
        string apiKey,
        ConversationResponseRequest request,
        InferenceGenerationOptions generationOptions,
        CancellationToken cancellationToken)
    {
        return StreamWithMetricsAsync(
            endpoint,
            apiKey,
            request,
            generationOptions,
            metricsObserver: null,
            cancellationToken);
    }

    internal async IAsyncEnumerable<ConversationResponseChunk> StreamWithMetricsAsync(
        Uri endpoint,
        string apiKey,
        ConversationResponseRequest request,
        InferenceGenerationOptions generationOptions,
        Action<LlamaCppGenerationMetrics>? metricsObserver,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(generationOptions);

        Uri requestUri = new(endpoint, "v1/chat/completions");
        string payload = JsonSerializer.Serialize(
            new ChatCompletionRequest(
                LlamaCppServerCommand.ModelAlias,
                MapMessages(request),
                Stream: true,
                new ChatCompletionStreamOptions(IncludeUsage: true),
                generationOptions.MaxOutputTokens,
                generationOptions.Temperature,
                generationOptions.TopP,
                generationOptions.TopK,
                generationOptions.Seed,
                ReasoningEffort: generationOptions.ReasoningEnabled == false
                    ? "none"
                    : null,
                ThinkingBudgetTokens: generationOptions.ReasoningEnabled switch
                {
                    false => 0,
                    true => generationOptions.ReasoningBudgetTokens,
                    _ => null,
                },
                ReasoningFormat: generationOptions.ReasoningEnabled == true
                    ? "deepseek"
                    : null),
            _jsonOptions);
        using HttpRequestMessage httpRequest = new(HttpMethod.Post, requestUri)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json"),
        };
        LlamaCppServerSecurity.Authorize(httpRequest, apiKey);

        // A generation POST is intentionally sent exactly once. A timeout, disconnect, or
        // server error can be ambiguous after request acceptance; only an explicit user Retry
        // may replay the unanswered turn.
        using HttpResponseMessage response = await SendGenerationOnceAsync(
            httpRequest,
            cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            string responseBody = await ReadFailureBodyAsync(
                response,
                cancellationToken).ConfigureAwait(false);
            throw CreateHttpFailure(response.StatusCode, responseBody);
        }

        await using Stream responseStream = await OpenResponseStreamAsync(
            response,
            cancellationToken).ConfigureAwait(false);
        using StreamReader reader = new(responseStream, Encoding.UTF8);
        LlamaCppGenerationMetrics? aggregateMetrics = null;

        while (true)
        {
            string? line = await ReadStreamLineAsync(
                reader,
                cancellationToken).ConfigureAwait(false);

            if (line is null)
            {
                throw new InferenceProviderException(
                    InferenceProviderFailureKind.Faulted,
                    "The local AI response ended unexpectedly. Try again, and review the provider if the problem continues.",
                    new InvalidDataException(
                        "llama.cpp streaming response ended before the [DONE] marker."));
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

            if (TryParseGenerationMetrics(data) is LlamaCppGenerationMetrics parsedMetrics)
            {
                aggregateMetrics = aggregateMetrics is null
                    ? parsedMetrics
                    : aggregateMetrics.Merge(parsedMetrics);
                metricsObserver?.Invoke(aggregateMetrics);
            }

            foreach (ConversationResponseChunk chunk in ParseResponseDeltas(data))
            {
                yield return chunk;
            }
        }
    }

    internal static ConversationResponseChunk[] ParseResponseDeltas(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        JsonDocument document;

        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException exception)
        {
            throw new InferenceProviderException(
                InferenceProviderFailureKind.Faulted,
                "The local AI runtime returned an invalid response. Try again, and review the provider if the problem continues.",
                new InvalidDataException(
                    "llama.cpp returned malformed JSON while streaming a response.",
                    exception));
        }

        using (document)
        {
            JsonElement root = document.RootElement;

            if (!root.TryGetProperty("choices", out JsonElement choices)
                || choices.ValueKind != JsonValueKind.Array
                || choices.GetArrayLength() == 0)
            {
                return [];
            }

            JsonElement firstChoice = choices[0];

            if (!firstChoice.TryGetProperty("delta", out JsonElement delta)
                || delta.ValueKind != JsonValueKind.Object)
            {
                return [];
            }

            List<ConversationResponseChunk> chunks = [];
            AddStringDelta(
                delta,
                "reasoning_content",
                ConversationResponseChunkKind.Reasoning,
                chunks);
            AddStringDelta(
                delta,
                "content",
                ConversationResponseChunkKind.Content,
                chunks);
            return [.. chunks];
        }
    }

    internal static LlamaCppGenerationMetrics? TryParseGenerationMetrics(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        JsonDocument document;

        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            return null;
        }

        using (document)
        {
            JsonElement root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            int? inputTokens = null;
            int? outputTokens = null;
            int? totalTokens = null;
            int? cachedInputTokens = null;
            TimeSpan? promptEvaluationDuration = null;
            TimeSpan? generationDuration = null;
            double? promptTokensPerSecond = null;
            double? generationTokensPerSecond = null;

            if (root.TryGetProperty("usage", out JsonElement usage)
                && usage.ValueKind == JsonValueKind.Object)
            {
                inputTokens = GetNonNegativeInt32(usage, "prompt_tokens");
                outputTokens = GetNonNegativeInt32(usage, "completion_tokens");
                totalTokens = GetNonNegativeInt32(usage, "total_tokens");

                if (usage.TryGetProperty("prompt_tokens_details", out JsonElement promptDetails)
                    && promptDetails.ValueKind == JsonValueKind.Object)
                {
                    cachedInputTokens = GetNonNegativeInt32(promptDetails, "cached_tokens");
                }
            }

            if (root.TryGetProperty("timings", out JsonElement timings)
                && timings.ValueKind == JsonValueKind.Object)
            {
                int? promptTokens = GetNonNegativeInt32(timings, "prompt_n");
                int? cachedTokens = GetNonNegativeInt32(timings, "cache_n");
                int? predictedTokens = GetNonNegativeInt32(timings, "predicted_n");

                cachedInputTokens ??= cachedTokens;

                if (inputTokens is null && promptTokens is int prompt)
                {
                    inputTokens = AddNonNegativeInt32(prompt, cachedTokens ?? 0);
                }

                outputTokens ??= predictedTokens;

                if (totalTokens is null
                    && inputTokens is int input
                    && outputTokens is int output)
                {
                    totalTokens = AddNonNegativeInt32(input, output);
                }

                promptEvaluationDuration = GetNonNegativeMilliseconds(timings, "prompt_ms");
                generationDuration = GetNonNegativeMilliseconds(timings, "predicted_ms");
                promptTokensPerSecond = GetNonNegativeDouble(timings, "prompt_per_second");
                generationTokensPerSecond = GetNonNegativeDouble(timings, "predicted_per_second");
            }

            if (inputTokens is int inputCount
                && outputTokens is int outputCount
                && totalTokens is int totalCount
                && totalCount != (long)inputCount + outputCount)
            {
                totalTokens = null;
            }

            if (cachedInputTokens is int cachedCount
                && inputTokens is int observedInputCount
                && cachedCount > observedInputCount)
            {
                cachedInputTokens = null;
            }

            if (inputTokens is null
                && outputTokens is null
                && totalTokens is null
                && cachedInputTokens is null
                && promptEvaluationDuration is null
                && generationDuration is null
                && promptTokensPerSecond is null
                && generationTokensPerSecond is null)
            {
                return null;
            }

            return new LlamaCppGenerationMetrics(
                inputTokens,
                outputTokens,
                totalTokens,
                cachedInputTokens,
                promptEvaluationDuration,
                generationDuration,
                promptTokensPerSecond,
                generationTokensPerSecond);
        }
    }

    private static int? AddNonNegativeInt32(int left, int right)
    {
        long total = (long)left + right;
        return total <= int.MaxValue ? (int)total : null;
    }

    private static int? GetNonNegativeInt32(JsonElement parent, string propertyName)
    {
        if (!parent.TryGetProperty(propertyName, out JsonElement value)
            || value.ValueKind != JsonValueKind.Number
            || !value.TryGetInt32(out int parsed)
            || parsed < 0)
        {
            return null;
        }

        return parsed;
    }

    private static double? GetNonNegativeDouble(JsonElement parent, string propertyName)
    {
        if (!parent.TryGetProperty(propertyName, out JsonElement value)
            || value.ValueKind != JsonValueKind.Number
            || !value.TryGetDouble(out double parsed)
            || double.IsNaN(parsed)
            || double.IsInfinity(parsed)
            || parsed < 0)
        {
            return null;
        }

        return parsed;
    }

    private static TimeSpan? GetNonNegativeMilliseconds(
        JsonElement parent,
        string propertyName)
    {
        double? milliseconds = GetNonNegativeDouble(parent, propertyName);

        return milliseconds is double value
            ? TimeSpan.FromMilliseconds(value)
            : null;
    }

    private static void AddStringDelta(
        JsonElement delta,
        string propertyName,
        ConversationResponseChunkKind kind,
        List<ConversationResponseChunk> chunks)
    {
        if (!delta.TryGetProperty(propertyName, out JsonElement value)
            || value.ValueKind != JsonValueKind.String)
        {
            return;
        }

        string? text = value.GetString();

        if (!string.IsNullOrEmpty(text))
        {
            chunks.Add(new ConversationResponseChunk(kind, text));
        }
    }

    private static ChatCompletionMessage[] MapMessages(
        ConversationResponseRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        List<ChatCompletionMessage> messages = new(request.Messages.Count + 1);

        if (request.SystemInstructions is not null)
        {
            messages.Add(new ChatCompletionMessage(
                "system",
                request.SystemInstructions));
        }

        messages.AddRange(request.Messages.Select(MapMessage));
        return [.. messages];
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

    private async Task<HttpResponseMessage> SendGenerationOnceAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        try
        {
            return await LlamaCppTimeoutGuard.RunAsync(
                token => _httpClient.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    token),
                _timeouts.ChatResponseHeaders,
                InferenceProviderFailureKind.Network,
                "The local AI server did not respond in time. Check the provider and try again.",
                cancellationToken)
                .ConfigureAwait(false);
        }
        catch (HttpRequestException exception)
        {
            throw new InferenceProviderException(
                InferenceProviderFailureKind.Network,
                "Alicia could not reach the local AI server. Check the provider and try again.",
                exception);
        }
    }

    private async Task<string> ReadFailureBodyAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        try
        {
            return await LlamaCppTimeoutGuard.RunAsync(
                token => response.Content.ReadAsStringAsync(token),
                _timeouts.ChatStreamIdle,
                InferenceProviderFailureKind.Network,
                "The local AI server stopped responding. Check the provider and try again.",
                cancellationToken)
                .ConfigureAwait(false);
        }
        catch (HttpRequestException exception)
        {
            throw new InferenceProviderException(
                InferenceProviderFailureKind.Network,
                "Alicia could not finish reading the local AI response. Check the provider and try again.",
                exception);
        }
        catch (IOException exception)
        {
            throw new InferenceProviderException(
                InferenceProviderFailureKind.Network,
                "Alicia could not finish reading the local AI response. Check the provider and try again.",
                exception);
        }
    }

    private async Task<Stream> OpenResponseStreamAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        try
        {
            return await LlamaCppTimeoutGuard.RunAsync(
                token => response.Content.ReadAsStreamAsync(token),
                _timeouts.ChatStreamIdle,
                InferenceProviderFailureKind.Network,
                "The local AI response did not begin in time. Check the provider and try again.",
                cancellationToken)
                .ConfigureAwait(false);
        }
        catch (HttpRequestException exception)
        {
            throw new InferenceProviderException(
                InferenceProviderFailureKind.Network,
                "Alicia could not open the local AI response stream. Check the provider and try again.",
                exception);
        }
        catch (IOException exception)
        {
            throw new InferenceProviderException(
                InferenceProviderFailureKind.Network,
                "Alicia could not open the local AI response stream. Check the provider and try again.",
                exception);
        }
    }

    private async Task<string?> ReadStreamLineAsync(
        StreamReader reader,
        CancellationToken cancellationToken)
    {
        try
        {
            return await LlamaCppTimeoutGuard.RunAsync(
                token => reader.ReadLineAsync(token).AsTask(),
                _timeouts.ChatStreamIdle,
                InferenceProviderFailureKind.Network,
                "The local AI response stopped arriving. Check the provider and try again.",
                cancellationToken)
                .ConfigureAwait(false);
        }
        catch (IOException exception)
        {
            throw new InferenceProviderException(
                InferenceProviderFailureKind.Network,
                "The local AI response stream was interrupted. Check the provider and try again.",
                exception);
        }
    }

    private static InferenceProviderException CreateHttpFailure(
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

        InvalidOperationException diagnostic = new(
            $"llama.cpp returned HTTP {(int)statusCode} ({statusCode}). {detail}");

        if ((int)statusCode is 400 or 413 or 422)
        {
            return new InferenceProviderException(
                InferenceProviderFailureKind.Model,
                "The local AI server rejected the model request. Review the model and generation settings, then try again.",
                diagnostic);
        }

        if (statusCode is HttpStatusCode.RequestTimeout
            or HttpStatusCode.BadGateway
            or HttpStatusCode.ServiceUnavailable
            or HttpStatusCode.GatewayTimeout)
        {
            return new InferenceProviderException(
                InferenceProviderFailureKind.Network,
                "The local AI server is temporarily unavailable. Check the provider and try again.",
                diagnostic);
        }

        return new InferenceProviderException(
            InferenceProviderFailureKind.Faulted,
            "The local AI server rejected the request. Review the provider and try again.",
            diagnostic);
    }

    private sealed record ChatCompletionRequest(
        string Model,
        IReadOnlyList<ChatCompletionMessage> Messages,
        bool Stream,
        [property: JsonPropertyName("stream_options")] ChatCompletionStreamOptions StreamOptions,
        [property: JsonPropertyName("max_tokens")] int? MaxTokens,
        double? Temperature,
        [property: JsonPropertyName("top_p")] double? TopP,
        [property: JsonPropertyName("top_k")] int? TopK,
        int? Seed,
        [property: JsonPropertyName("reasoning_effort")] string? ReasoningEffort,
        [property: JsonPropertyName("thinking_budget_tokens")] int? ThinkingBudgetTokens,
        [property: JsonPropertyName("reasoning_format")] string? ReasoningFormat);

    private sealed record ChatCompletionStreamOptions(
        [property: JsonPropertyName("include_usage")] bool IncludeUsage);

    private sealed record ChatCompletionMessage(
        string Role,
        string Content);
}
