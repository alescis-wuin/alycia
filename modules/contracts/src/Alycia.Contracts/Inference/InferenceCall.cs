using System.Text.Json;
using Alycia.Contracts.Chat;

namespace Alycia.Contracts.Inference;

public sealed record InferenceCall(GenerationParameters Generation, int SafetyTokens, bool AdaptiveInferenceEnabled,
    ITextTokenCounter? TokenCounter)
{
    // Provider metadata is resolved before dispatch. The budget is checked again after applying a smaller model limit.
    public static async Task<(string? Identity, InferenceCall Call)> PrepareAuxiliaryAsync(IChatModel model, string name,
        int context, int output, int safety, int timeout, string purpose, JsonElement? schema, CancellationToken ct)
    {
        var responseSchema = schema?.Clone();
        var metadata = await model.InspectAsync(name, "auto", ct);
        if (metadata.ContextLimit is int limit) context = Math.Min(context, limit);
        return (metadata.Identity, new(new(context, output, false, 0, 0, timeout, purpose, responseSchema),
            safety, false, metadata.Inspected ? metadata.Counter : null));
    }

    public InferenceCall CheckWindow()
    {
        if (Generation.ContextTokens is < 1024 or > 131072 || Generation.MaxOutputTokens < 1 || SafetyTokens < 128
            || (long)Generation.MaxOutputTokens + SafetyTokens >= Generation.ContextTokens)
            throw new ArgumentException("Configuration Chat invalide : vérifiez fenêtre, contexte, réserve et délai.");
        return this;
    }

    public static InferenceCall Default { get; } = Capture(new ChatSettings());

    public static InferenceCall Capture(ChatSettings settings, string purpose = "chat", JsonElement? schema = null,
        ITextTokenCounter? counter = null) => new(
        new(settings.ContextTokens, settings.MaxOutputTokens, settings.GenerateUntilStop, settings.Temperature,
            settings.Seed, settings.TimeoutSeconds, purpose, schema?.Clone()),
        settings.SafetyTokens, settings.AdaptiveInferenceEnabled, counter);
}

public static class PromptTokens
{
    public static int Count(string text, ITextTokenCounter? counter) =>
        counter is null ? SettingsInput.Estimate(text) : checked(counter.Count(text) + 32);

    public static string Fit(string text, int budget, ITextTokenCounter? counter)
    {
        if (Count(text, counter) <= budget) return text;
        var low = 0; var high = text.Length;
        while (low < high)
        {
            var mid = low + (high - low + 1) / 2;
            if (Count(text[..mid], counter) <= budget) low = mid; else high = mid - 1;
        }
        if (low > 0 && low < text.Length && char.IsLowSurrogate(text[low])) low--;
        return text[..low];
    }

    public static string Description(ITextTokenCounter? counter) =>
        counter?.Name ?? "UTF-8 conservateur — tokenizer non disponible pour ce modèle";

    public static int InputCost(IReadOnlyList<PromptMessage> messages, ITextTokenCounter? counter, bool tools)
    {
        var cost = messages.Sum(m => Count(m.Content, counter)
            + (string.IsNullOrEmpty(m.Thinking) ? 0 : Count(m.Thinking, counter))
            + (m.ToolName is null ? 0 : Count(m.ToolName, counter))
            + (m.ToolCalls is null ? 0 : Count(JsonSerializer.Serialize(m.ToolCalls, ProjectToolCatalog.Json), counter)));
        return cost + (tools ? Count(JsonSerializer.Serialize(ProjectToolCatalog.OllamaTools(), ProjectToolCatalog.Json), counter) : 0);
    }
}
