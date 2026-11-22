using Alycia.Contracts.Chat;
using Alycia.Contracts.Inference;

namespace Alycia.Contracts.Context;

public enum TurnContextMode { Send, Preview, PrepareMemory }

public sealed record TurnContextBudget(ContextParameters Options, int ToolReserve)
{
    public static ContextParameters WithToolReserve(ContextParameters settings, int protectedTokens = 0)
    {
        return settings.WithSafetyTokens(ToolWindowBudget.SafetyTokens(settings, settings.Count, protectedTokens));
    }
}

public sealed record TurnContextResult(ContextWindow Window, PreparedContext? Prepared);

public interface ITurnContext
{
    Task<TurnContextBudget> AllocateAsync(ContextConversation conversation, string text, ContextParameters options, bool useTools, CancellationToken ct);
    Task<TurnContextResult> BuildAsync(ContextConversation conversation, string text, TurnContextBudget budget, IChatModel model,
        IModelMeter measured, TurnContextMode mode, Func<string, Task>? progress, CancellationToken ct);
}
