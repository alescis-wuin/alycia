using System.Collections.Immutable;
using Alycia.Contracts.Context;

namespace Alycia.Modules.Context;

public sealed class SlidingHistory : ISlidingHistory
{
    public HistorySelection Select(ImmutableArray<WindowMessage> history, HistoryBudget budget, Func<string, int> count)
    {
        if (history.Length % 2 != 0) throw new InvalidOperationException("Historique de conversation incomplet.");
        var used = budget.PromptTokens;
        if (used > budget.Available)
            throw new ArgumentException("Le message et l’instruction système dépassent le budget de contexte. Raccourcissez-les.");
        var historyCost = 0;
        var start = history.Length;
        for (var turns = 0; turns < budget.MaxTurns && start >= 2; turns++)
        {
            var user = history[start - 2];
            var assistant = history[start - 1];
            if (user.Role != "user" || assistant.Role != "assistant" || user.RequestId != assistant.RequestId
                || assistant.Sequence != user.Sequence + 1 || user.Owner != assistant.Owner)
                throw new InvalidOperationException("Historique de conversation incohérent.");
            var cost = count(user.Content) + count(assistant.Content);
            if (used + cost > budget.Available || budget.MaxHistoryTokens > 0 && historyCost + cost > budget.MaxHistoryTokens) break;
            historyCost += cost;
            used += cost;
            start -= 2;
        }
        return new(start, used);
    }
}
