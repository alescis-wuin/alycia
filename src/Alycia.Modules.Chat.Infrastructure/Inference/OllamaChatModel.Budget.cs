using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Text.Json;
using Alycia.Modules.Chat.Application;

namespace Alycia.Modules.Chat.Infrastructure;

public sealed partial class OllamaChatModel
{
    public async Task<long?> ResidentVramAsync(string model, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromMilliseconds(250));
        try
        {
            using var response = await http.GetAsync("api/ps", timeout.Token);
            if (!response.IsSuccessStatusCode) return null;
            using var json = JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync(timeout.Token));
            foreach (var item in json.RootElement.GetProperty("models").EnumerateArray())
                if (item.GetProperty("name").GetString() == model && item.TryGetProperty("size_vram", out var bytes) && bytes.TryGetInt64(out var size)) return size;
        }
        catch (Exception e) when (e is HttpRequestException or JsonException or KeyNotFoundException or OperationCanceledException or InvalidOperationException) { }
        return null;
    }
    private sealed record CounterResult(ITextTokenCounter? Counter, int? ContextLimit);
    private static readonly ConcurrentDictionary<string, CounterResult> Counters = new();
    public async Task<ChatOptions> PrepareOptionsAsync(string model, ChatOptions settings, CancellationToken ct)
    {
        // Fail before progress events start the HTTP stream, including with conservative counting.
        var result = settings.Copy();
        await SupportsToolsAsync(model, ct);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        try
        {
            // Identity is fetched anew so a model alias cannot reuse another model's vocabulary.
            using var tags = await http.GetAsync("api/tags", timeout.Token);
            tags.EnsureSuccessStatusCode();
            using var list = JsonDocument.Parse(await tags.Content.ReadAsStringAsync(timeout.Token));
            var item = list.RootElement.GetProperty("models").EnumerateArray().FirstOrDefault(x => x.GetProperty("name").GetString() == model);
            if (item.ValueKind == JsonValueKind.Undefined || !item.TryGetProperty("digest", out var digest) || string.IsNullOrWhiteSpace(digest.GetString())) return result;
            var identity = model + ":" + digest.GetString();
            result.ModelIdentity = identity;
            var key = http.BaseAddress + "|" + identity + "|" + result.TokenCounting;
            if (Counters.TryGetValue(key, out var existing))
            {
                result.TokenCounter = existing.Counter;
                if (existing.ContextLimit is int maximum) result.ContextTokens = Math.Min(result.ContextTokens, maximum);
                return result;
            }
            using var request = new HttpRequestMessage(HttpMethod.Post, "api/show") { Content = JsonContent.Create(new { model, verbose = result.TokenCounting == "auto" }) };
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            response.EnsureSuccessStatusCode();
            await response.Content.LoadIntoBufferAsync(32 * 1024 * 1024, timeout.Token);
            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
            using var json = await JsonDocument.ParseAsync(stream, cancellationToken: timeout.Token);
            var hasMetadata = json.RootElement.TryGetProperty("model_info", out var info);
            var counter = hasMetadata && result.TokenCounting == "auto" ? QwenTokenCounter.FromMetadata(info) : null;
            int? contextLimit = null;
            if (hasMetadata && info.TryGetProperty("general.architecture", out var architecture)
                && info.TryGetProperty(architecture.GetString() + ".context_length", out var length)
                && length.TryGetInt32(out var maximumContext) && maximumContext > 0)
                contextLimit = maximumContext;
            if (Counters.Count >= 8) Counters.Clear();
            Counters[key] = new(counter, contextLimit); result.TokenCounter = counter;
            if (contextLimit is int limit) result.ContextTokens = Math.Min(result.ContextTokens, limit);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { }
        catch (Exception e) when (e is HttpRequestException or JsonException or ArgumentException or InvalidOperationException or KeyNotFoundException) { }
        return result;
    }
}
