using System.Collections.Immutable;
using Alycia.Contracts.Context;

namespace Alycia.Modules.Context;

public static class SlidingContext
{
    // Conservative UTF-8 byte estimate, not a model-specific tokenizer.
    // Keep extra room for the model's chat template and generated answer.
    public static int EstimateTokens(string content) => SettingsInput.Estimate(content);

    public static ContextWindow Build(ISlidingHistory policy, string systemPrompt, IReadOnlyList<HistoryMessage> history,
        string userMessage, ContextParameters options)
    {
        var messages = history.Select(m => new WindowMessage(m.ConversationId, m.RequestId, m.Sequence, m.Role, m.Content)).ToImmutableArray();
        var selection = policy.Select(messages, new(options.AvailableInputTokens, options.PromptCost(systemPrompt, userMessage),
            options.History.MaxHistoryTurns, options.History.Budget.History), text => options.Count(text));
        var selected = new List<PromptMessage>();
        if (systemPrompt.Length > 0) selected.Add(new("system", systemPrompt));
        selected.AddRange(messages.Skip(selection.Start).Select(m => new PromptMessage(m.Role, m.Content)));
        selected.Add(new("user", userMessage));
        return new(selected, messages.Length - selection.Start, selection.UsedTokens);
    }
}
