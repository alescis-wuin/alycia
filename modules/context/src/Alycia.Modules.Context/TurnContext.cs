using Alycia.Contracts.Context;

namespace Alycia.Modules.Context;

public sealed class TurnContext(IContextMemory rules, IConversationContextReader repository, Alycia.Contracts.Context.ISlidingHistory windowPolicy) : ITurnContext
{
    public Task<TurnContextBudget> AllocateAsync(ContextConversation conversation, string text, ContextParameters options, bool useTools, CancellationToken ct) =>
        AllocateAsync(repository, conversation, text, options, useTools, ct);

    internal static async Task<TurnContextBudget> AllocateAsync(IConversationContextReader repository, ContextConversation conversation, string text, ContextParameters options, bool useTools, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!useTools) return new(options, 0);

        var protectedTokens = options.Count(text) + options.Count(conversation.SystemText);
        if (options.History.AutoSummarize)
        {
            var recent = await repository.RecentAsync(conversation.Id, options.History.ProtectedHistoryTurns * 2, null, ct);
            protectedTokens += recent.Sum(message => options.Count(message.Content));
        }
        var reserved = TurnContextBudget.WithToolReserve(options, protectedTokens);
        return new(reserved, reserved.SafetyTokens - options.SafetyTokens);
    }

    public async Task<TurnContextResult> BuildAsync(ContextConversation conversation, string text, TurnContextBudget budget, IChatModel model,
        IModelMeter measured, TurnContextMode mode, Func<string, Task>? progress, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var options = budget.Options;
        var generate = mode != TurnContextMode.Preview;
        PreparedContext? prepared = null;
        ContextWindow window;
        if (mode == TurnContextMode.PrepareMemory || options.History.AutoSummarize || options.Evidence.Sources.Recall is not null)
        {
            prepared = await ConversationContext.BuildAsync(rules, repository, generate ? measured : model, conversation, text, options,
                generate, generate ? progress : null, ct);
            window = prepared.Context;
        }
        else
        {
            var limit = options.History.MaxHistoryTurns * 2;
            var history = await repository.RecentAsync(conversation.Id, limit, null, ct);
            var files = await repository.ContextFilesAsync(conversation.Id, Math.Max(1, conversation.MessageCount - limit + 1), ct);
            window = FileContext.Build(windowPolicy, conversation.SystemText, history, text, options, files, conversation.MessageCount + 1);
        }

        window = await ContextGuides.AddAsync(window, options, measured, generate, ct);
        if (mode != TurnContextMode.Send)
            window = window with { Route = options.Evidence.Sources.Query.Route, ToolReserve = budget.ToolReserve };
        else if (budget.ToolReserve != 0)
            window = window with { ToolReserve = budget.ToolReserve };
        return new(window, prepared);
    }
}
