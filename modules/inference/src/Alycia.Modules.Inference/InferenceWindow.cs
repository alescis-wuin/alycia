namespace Alycia.Modules.Inference;

public static class InferenceWindow
{
    public static InferenceCall ForCall(InferenceCall options, IReadOnlyList<PromptMessage> messages, bool tools)
    {
        if (!options.AdaptiveInferenceEnabled || options.Generation.GenerateUntilStop) return options;
        var need = (long)PromptTokens.InputCost(messages, options.TokenCounter, tools) + options.Generation.MaxOutputTokens + options.SafetyTokens;
        if (need > options.Generation.ContextTokens) throw new ArgumentException("L’appel dépasse la fenêtre effective du modèle et ses réserves.");
        var size = 1024;
        while (size < need) size *= 2;
        return options with { Generation = options.Generation with { ContextTokens = Math.Min(options.Generation.ContextTokens, size) } };
    }
}
