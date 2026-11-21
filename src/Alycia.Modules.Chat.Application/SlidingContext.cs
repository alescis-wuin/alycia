using System.Collections.Immutable;
using Alycia.Contracts.Context;
using Alycia.Modules.Chat.Domain;

namespace Alycia.Modules.Chat.Application;

public static class SlidingContext
{
    // Conservative UTF-8 byte estimate, not a model-specific tokenizer.
    // Keep extra room for the model's chat template and generated answer.
    public static int EstimateTokens(string content) => SettingsInput.Estimate(content);

    public static void ValidatePrompt(string system, string user, ChatOptions options)
    {
        options.Validate();
        if (PromptCost(system, user, options) > options.AvailableInputTokens)
            throw new ArgumentException("Le message et l’instruction système dépassent le budget de contexte. Raccourcissez-les.");
    }

    private static int PromptCost(string system, string user, ChatOptions options) =>
        TokenBudget.Count(user, options) + (system.Length == 0 ? 0 : TokenBudget.Count(system, options));

    public static ContextWindow Build(ISlidingHistory policy, string systemPrompt, IReadOnlyList<ChatMessage> history,
        string userMessage, ChatOptions options)
    {
        options.Validate();
        var messages = history.Select(m => new WindowMessage(m.ConversationId, m.RequestId, m.Sequence, m.Role, m.Content)).ToImmutableArray();
        var selection = policy.Select(messages, new(options.AvailableInputTokens, PromptCost(systemPrompt, userMessage, options),
            options.MaxHistoryTurns, options.HistoryContextTokens), text => TokenBudget.Count(text, options));
        var selected = new List<PromptMessage>();
        if (systemPrompt.Length > 0) selected.Add(new("system", systemPrompt));
        selected.AddRange(messages.Skip(selection.Start).Select(m => new PromptMessage(m.Role, m.Content)));
        selected.Add(new("user", userMessage));
        return new(selected, messages.Length - selection.Start, selection.UsedTokens);
    }
}
