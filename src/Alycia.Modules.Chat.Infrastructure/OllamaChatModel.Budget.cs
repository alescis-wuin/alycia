using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Alycia.Modules.Chat.Application;

namespace Alycia.Modules.Chat.Infrastructure;

public sealed partial class OllamaChatModel
{
    private static readonly ConcurrentDictionary<string, ITextTokenCounter> Counters = new();
    public async Task<ChatOptions> PrepareOptionsAsync(string model, ChatOptions settings, CancellationToken ct)
    {
        // Fail before progress events start the HTTP stream, including with conservative counting.
        await SupportsToolsAsync(model, ct);
        var result = settings.Copy();
        if (settings.TokenCounting == "conservative") return result;
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
            var key = http.BaseAddress + "|" + identity;
            if (Counters.TryGetValue(key, out var existing)) { result.TokenCounter = existing; return result; }
            using var request = new HttpRequestMessage(HttpMethod.Post, "api/show") { Content = JsonContent.Create(new { model, verbose = true }) };
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
            using var json = JsonDocument.Parse(await FileImport.ReadBoundedAsync(stream, 32 * 1024 * 1024, timeout.Token));
            if (json.RootElement.TryGetProperty("model_info", out var info) && QwenTokenCounter.FromMetadata(info) is { } counter)
            {
                if (Counters.Count >= 8) Counters.Clear();
                Counters[key] = counter; result.TokenCounter = counter;
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { }
        catch (Exception e) when (e is HttpRequestException or JsonException or ArgumentException or InvalidOperationException or KeyNotFoundException) { }
        return result;
    }
}
