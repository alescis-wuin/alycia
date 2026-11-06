using System.Text;
using Alycia.Modules.Chat.Domain;

namespace Alycia.Modules.Chat.Application;

public static class SlidingContext
{
    // Conservative UTF-8 byte estimate, not a model-specific tokenizer.
    // Keep extra room for the model's chat template and generated answer.
    public static int EstimateTokens(string content) => Encoding.UTF8.GetByteCount(content) + 16;

    public static ContextWindow Build(string systemPrompt, IReadOnlyList<ChatMessage> history,
        string userMessage, ChatOptions options)
    {
        options.Validate();
        var budget = options.ContextTokens - options.MaxOutputTokens - options.SafetyTokens;
        var used = EstimateTokens(userMessage)
            + (systemPrompt.Length == 0 ? 0 : EstimateTokens(systemPrompt));
        if (used > budget)
        {
            throw new ArgumentException("Le message et l’instruction système dépassent le budget de contexte. Raccourcissez-les.");
        }

        var selected = new List<PromptMessage>();
        // Add complete recent turns only, in chronological order. Never keep
        // an assistant answer without its user question or skip a newer turn.
        var start = history.Count;
        for (var turns = 0; turns < options.MaxHistoryTurns && start >= 2; turns++)
        {
            var user = history[start - 2];
            var assistant = history[start - 1];
            if (user.Role != "user" || assistant.Role != "assistant"
                || user.RequestId != assistant.RequestId)
            {
                throw new InvalidOperationException("Historique de conversation incohérent.");
            }

            var cost = EstimateTokens(user.Content) + EstimateTokens(assistant.Content);
            if (used + cost > budget)
            {
                break;
            }

            used += cost;
            start -= 2;
        }

        if (systemPrompt.Length > 0)
        {
            selected.Add(new("system", systemPrompt));
        }

        selected.AddRange(history.Skip(start).Select(message => new PromptMessage(message.Role, message.Content)));
        selected.Add(new("user", userMessage));
        return new(selected, history.Count - start, used);
    }
}
