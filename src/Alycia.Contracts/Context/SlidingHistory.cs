using System.Collections.Immutable;

namespace Alycia.Contracts.Context;

public sealed record WindowMessage(Guid Owner, Guid RequestId, int Sequence, string Role, string Content);
public sealed record HistoryBudget(int Available, int PromptTokens, int MaxTurns, int MaxHistoryTokens);
public readonly record struct HistorySelection(int Start, int UsedTokens);

public interface ISlidingHistory
{
    HistorySelection Select(ImmutableArray<WindowMessage> history, HistoryBudget budget, Func<string, int> count);
}
