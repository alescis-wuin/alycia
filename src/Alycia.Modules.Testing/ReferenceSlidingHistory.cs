using System.Collections.Immutable;
using Alycia.Contracts.Context;

namespace Alycia.Modules.Testing;

public sealed class ReferenceSlidingHistory : ISlidingHistory
{
    public HistorySelection Select(ImmutableArray<WindowMessage> messages, HistoryBudget budget, Func<string, int> count)
    {
        if (messages.Length % 2 != 0) throw new InvalidOperationException("Paire incomplète.");
        if (budget.Available < budget.PromptTokens) throw new ArgumentException("Prompt hors budget.");
        var capacity = budget.Available - budget.PromptTokens;
        if (budget.MaxHistoryTokens > 0) capacity = Math.Min(capacity, budget.MaxHistoryTokens);
        var size = 0;
        var cost = 0;
        foreach (var pair in messages.Chunk(2).Reverse().Take(budget.MaxTurns))
        {
            var user = pair[0];
            var answer = pair[1];
            if (user.Owner != answer.Owner || user.RequestId != answer.RequestId || user.Role != "user" ||
                answer.Role != "assistant" || answer.Sequence != user.Sequence + 1) throw new InvalidOperationException("Paire incohérente.");
            var tokens = count(user.Content) + count(answer.Content);
            if (tokens > capacity - cost) break;
            size += 2;
            cost += tokens;
        }
        return new(messages.Length - size, budget.PromptTokens + cost);
    }
}
