using System.Collections.Immutable;
using Alycia.Contracts.Context;

namespace Alycia.Modules.Testing;

public sealed class FixtureTurnContext(IConversationContextReader reader, ISlidingHistory history,
    Func<ContextConversation, string, TurnContextBudget, TurnContextMode, CancellationToken, Task<TurnContextResult>>? prepare = null) : ITurnContext
{
    public async Task<TurnContextBudget> AllocateAsync(ContextConversation conversation, string text, ContextParameters options, bool useTools, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!useTools) return new(options, 0);
        var protectedTokens = options.Count(conversation.SystemText) + options.Count(text);
        if (options.History.AutoSummarize)
            protectedTokens += (await reader.RecentAsync(conversation.Id, options.History.ProtectedHistoryTurns * 2, null, ct)).Sum(m => options.Count(m.Content));
        var reserved = TurnContextBudget.WithToolReserve(options, protectedTokens);
        return new(reserved, reserved.SafetyTokens - options.SafetyTokens);
    }
    public async Task<TurnContextResult> BuildAsync(ContextConversation conversation, string text, TurnContextBudget budget, IChatModel model,
        IModelMeter measured, TurnContextMode mode, Func<string, Task>? progress, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (prepare is not null) return await prepare(conversation, text, budget, mode, ct);
        var rows = await reader.RecentAsync(conversation.Id, budget.Options.History.MaxHistoryTurns * 2, null, ct);
        var candidates = rows.Select(m => new WindowMessage(m.ConversationId, m.RequestId, m.Sequence, m.Role, m.Content)).ToImmutableArray();
        var selection = history.Select(candidates, new(budget.Options.AvailableInputTokens, budget.Options.PromptCost(conversation.SystemText, text),
            budget.Options.History.MaxHistoryTurns, budget.Options.History.Budget.History), budget.Options.Count);
        var chosen = candidates[selection.Start..];
        var messages = new List<PromptMessage>();
        if (conversation.SystemText.Length > 0) messages.Add(new("system", conversation.SystemText));
        messages.AddRange(chosen.Select(m => new PromptMessage(m.Role, m.Content)));
        messages.Add(new("user", text));
        var window = new ContextWindow(PromptSnapshot.Capture(messages), chosen.Length, selection.UsedTokens, ToolReserve: budget.ToolReserve,
            Route: mode == TurnContextMode.Send ? null : budget.Options.Evidence.Sources.Query.Route);
        return new(window, mode == TurnContextMode.PrepareMemory || budget.Options.History.AutoSummarize || budget.Options.Evidence.Sources.Recall is not null
            ? new(window, conversation.MemoryText, conversation.MemoryThroughSequence, conversation.MemorySourceHash, false) : null);
    }
}
