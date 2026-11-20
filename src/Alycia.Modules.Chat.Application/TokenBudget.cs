using System.Text;
using System.Text.Json.Serialization;

namespace Alycia.Modules.Chat.Application;

public interface ITextTokenCounter
{
    string Name { get; }
    int Count(string text);
}

public static class TokenBudget
{
    public static GenerationUsage? ReadUsage(string json)
    {
        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(json);
            return document.RootElement.TryGetProperty("usage", out var usage)
                ? System.Text.Json.JsonSerializer.Deserialize<GenerationUsage>(usage.GetRawText(), ProjectToolCatalog.Json) : null;
        }
        catch (System.Text.Json.JsonException) { return null; }
    }
    // Framing is an explicit conservative allowance, not a claim of exact template rendering.
    public static int Count(string text, ChatOptions options) => options.TokenCounter is null
        ? SlidingContext.EstimateTokens(text) : checked(options.TokenCounter.Count(text) + 32);
    public static string Description(ChatOptions options) => options.TokenCounter?.Name
        ?? "UTF-8 conservateur — tokenizer non disponible pour ce modèle";
}

public sealed record ModelCallUsage(string Purpose, int EstimatedInputTokens, double ElapsedMilliseconds, GenerationStats? Stats, double? FirstContentMilliseconds = null, int? EffectiveContextTokens = null, long? ApiWorkingSetBytes = null, long? ModelVramBytes = null);
public sealed record GenerationUsage(IReadOnlyList<ModelCallUsage> Calls, string? ModelIdentity, string Counter,
    long? InputTokens, long? OutputTokens, double ElapsedMilliseconds, bool Complete);

// Per operation, never static: simultaneous conversations cannot mix their measurements.
public sealed class MeteredChatModel(IChatModel inner) : IChatModel
{
    private readonly List<ModelCallUsage> _calls = [];
    public void AddCalls(IEnumerable<ModelCallUsage> calls) => _calls.AddRange(calls);
    public GenerationUsage Snapshot(ChatOptions options) => new(_calls.ToArray(), options.ModelIdentity, TokenBudget.Description(options),
        _calls.All(c => c.Stats?.PromptTokens is not null) ? _calls.Sum(c => (long)c.Stats!.PromptTokens!.Value) : null,
        _calls.All(c => c.Stats?.OutputTokens is not null) ? _calls.Sum(c => (long)c.Stats!.OutputTokens!.Value) : null,
        _calls.Sum(c => c.ElapsedMilliseconds), _calls.All(c => c.Stats?.PromptTokens is not null && c.Stats.OutputTokens is not null));
    public Task<ChatOptions> PrepareOptionsAsync(string model, ChatOptions settings, CancellationToken ct) => inner.PrepareOptionsAsync(model, settings, ct);
    public Task<bool> SupportsToolsAsync(string model, CancellationToken ct) => inner.SupportsToolsAsync(model, ct);
    public Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken ct) => inner.ListModelsAsync(ct);
    public Task<string> GenerateAsync(string model, IReadOnlyList<PromptMessage> messages, CancellationToken ct) => GenerateAsync(model, messages, new(), ct);
    public async Task<string> GenerateAsync(string model, IReadOnlyList<PromptMessage> messages, ChatOptions options, CancellationToken ct) =>
        (await Measure(effective => inner.GenerateReplyAsync(model, messages, effective, ct), model, messages, options, ct)).Content;
    public Task<GenerationReply> StreamAsync(string model, IReadOnlyList<PromptMessage> messages, ChatOptions options, Func<ModelDelta, Task> delta, CancellationToken ct) =>
        MeasureStreaming((observer, effective) => inner.StreamAsync(model, messages, effective, observer, ct), delta, model, messages, options, ct);
    public Task<GenerationReply> StreamWithToolsAsync(string model, IReadOnlyList<PromptMessage> messages, ChatOptions options, Func<ModelDelta, Task> delta, CancellationToken ct) =>
        MeasureStreaming((observer, effective) => inner.StreamWithToolsAsync(model, messages, effective, observer, ct), delta, model, messages, options, ct, "tools");
    private Task<GenerationReply> MeasureStreaming(Func<Func<ModelDelta, Task>, ChatOptions, Task<GenerationReply>> call,
        Func<ModelDelta, Task> delta, string model, IReadOnlyList<PromptMessage> messages, ChatOptions options, CancellationToken ct, string? purpose = null)
    {
        var timer = System.Diagnostics.Stopwatch.StartNew(); double? first = null;
        return Measure(effective => call(value =>
        {
            if (value.Content.Length > 0) first ??= timer.Elapsed.TotalMilliseconds;
            return delta(value);
        }, effective), model, messages, options, ct, purpose, () => first);
    }
    private async Task<GenerationReply> Measure(Func<ChatOptions, Task<GenerationReply>> call, string model, IReadOnlyList<PromptMessage> messages, ChatOptions options, CancellationToken ct, string? purpose = null, Func<double?>? firstContent = null)
    {
        var timer = System.Diagnostics.Stopwatch.StartNew();
        GenerationReply? reply = null;
        GenerationStats? failureStats = null;
        var effective = InferenceWindow.ForCall(options, messages, purpose == "tools");
        try { reply = await call(effective); return reply; }
        catch (ChatModelException exception) { failureStats = exception.Stats; throw; }
        finally
        {
            var duration = timer.Elapsed.TotalMilliseconds;
            var vram = options.AdaptiveInferenceEnabled && reply is not null && !ct.IsCancellationRequested ? await inner.ResidentVramAsync(model, ct) : null;
            _calls.Add(new(purpose ?? options.CallPurpose, InferenceWindow.InputCost(messages, options, purpose == "tools"), duration,
                reply?.Stats ?? failureStats, firstContent?.Invoke(), effective.ContextTokens, options.AdaptiveInferenceEnabled ? Environment.WorkingSet : null, vram));
        }
    }
}
