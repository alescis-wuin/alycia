using Alycia.Modules.Chat.Domain;

namespace Alycia.Modules.Chat.Application.Context;

public enum TurnContextMode { Send, Preview, PrepareMemory }

public sealed record TurnContextBudget(ChatOptions Options, int ToolReserve)
{
    public static ChatOptions WithToolReserve(ChatOptions settings, int protectedTokens = 0)
    {
        var options = settings.Copy();
        options.SafetyTokens = ToolWindowBudget.SafetyTokens(settings, text => TokenBudget.Count(text, settings), protectedTokens);
        return options;
    }
}

public sealed record TurnContextResult(ContextWindow Window, PreparedContext? Prepared);

public interface ITurnContext
{
    Task<TurnContextBudget> AllocateAsync(Conversation conversation, string text, ChatOptions options, bool useTools, CancellationToken ct);
    Task<TurnContextResult> BuildAsync(Conversation conversation, string text, TurnContextBudget budget, IChatModel model,
        MeteredChatModel measured, TurnContextMode mode, Func<string, Task>? progress, CancellationToken ct);
}

public sealed class TurnContext(IConversationContextReader repository, Alycia.Contracts.Context.ISlidingHistory windowPolicy) : ITurnContext
{
    public Task<TurnContextBudget> AllocateAsync(Conversation conversation, string text, ChatOptions options, bool useTools, CancellationToken ct) =>
        AllocateAsync(repository, conversation, text, options, useTools, ct);

    internal static async Task<TurnContextBudget> AllocateAsync(IConversationContextReader repository, Conversation conversation, string text, ChatOptions options, bool useTools, CancellationToken ct)
    {
        if (!useTools) return new(options, 0);

        var protectedTokens = TokenBudget.Count(text, options) + TokenBudget.Count(ConversationConfiguration.System(conversation, options), options);
        if (options.AutoSummarize)
        {
            var recent = await repository.MessagesAsync(conversation.Id, options.ProtectedHistoryTurns * 2, null, ct);
            protectedTokens += recent.Sum(message => TokenBudget.Count(message.Content, options));
        }
        var reserved = TurnContextBudget.WithToolReserve(options, protectedTokens);
        return new(reserved, reserved.SafetyTokens - options.SafetyTokens);
    }

    public async Task<TurnContextResult> BuildAsync(Conversation conversation, string text, TurnContextBudget budget, IChatModel model,
        MeteredChatModel measured, TurnContextMode mode, Func<string, Task>? progress, CancellationToken ct)
    {
        var options = budget.Options;
        var generate = mode != TurnContextMode.Preview;
        PreparedContext? prepared = null;
        ContextWindow window;
        if (mode == TurnContextMode.PrepareMemory || options.AutoSummarize || options.Recall is not null)
        {
            prepared = await ConversationContext.BuildAsync(repository, generate ? measured : model, conversation, text, options,
                generate, generate ? progress : null, ct);
            window = prepared.Context;
        }
        else
        {
            var limit = options.MaxHistoryTurns * 2;
            var history = await repository.MessagesAsync(conversation.Id, limit, null, ct);
            var files = await repository.ContextFilesAsync(conversation.Id, Math.Max(1, conversation.MessageCount - limit + 1), ct);
            window = FileContext.Build(windowPolicy, ConversationConfiguration.System(conversation, options), history, text, options, files, conversation.MessageCount + 1);
        }

        window = await ContextGuides.AddAsync(window, options, measured, generate, ct);
        if (mode != TurnContextMode.Send)
            window = window with { Route = options.Route, ToolReserve = budget.ToolReserve };
        else if (budget.ToolReserve != 0)
            window = window with { ToolReserve = budget.ToolReserve };
        return new(window, prepared);
    }
}
