using System.Text.Json;
using Alycia.Contracts.Chat;

namespace Alycia.Contracts.Inference;

public sealed record InferenceCall(GenerationParameters Generation, int SafetyTokens, bool AdaptiveInferenceEnabled,
    ITextTokenCounter? TokenCounter)
{
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
