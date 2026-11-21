using System.Text.Json;

namespace Alycia.Modules.Chat.Application;

public static class InferenceWindow
{
    public static int InputCost(IReadOnlyList<PromptMessage> messages, ChatOptions options, bool tools)
    {
        var cost = messages.Sum(m => TokenBudget.Count(m.Content, options)
            + (string.IsNullOrEmpty(m.Thinking) ? 0 : TokenBudget.Count(m.Thinking, options))
            + (m.ToolName is null ? 0 : TokenBudget.Count(m.ToolName, options))
            + (m.ToolCalls is null ? 0 : TokenBudget.Count(JsonSerializer.Serialize(m.ToolCalls, ProjectToolCatalog.Json), options)));
        return cost + (tools ? TokenBudget.Count(JsonSerializer.Serialize(ProjectToolCatalog.OllamaTools(), ProjectToolCatalog.Json), options) : 0);
    }
    public static ChatOptions ForCall(ChatOptions options, IReadOnlyList<PromptMessage> messages, bool tools)
    {
        if (!options.AdaptiveInferenceEnabled || options.GenerateUntilStop) return options;
        var need = (long)InputCost(messages, options, tools) + options.MaxOutputTokens + options.SafetyTokens;
        if (need > options.ContextTokens) throw new ArgumentException("L’appel dépasse la fenêtre effective du modèle et ses réserves.");
        var size = 1024;
        while (size < need) size *= 2;
        var copy = options.Copy(); copy.ContextTokens = Math.Min(options.ContextTokens, size);
        return copy;
    }
}
