using System.Security.Cryptography;
using System.Text;
using Alycia.Modules.Chat.Domain;

namespace Alycia.Modules.Chat.Application;

public sealed record PreparedContext(ContextWindow Context, string Memory, int Through, string SourceHash, bool Changed);

public static class RecursiveContext
{
    private const string MemoryLabel = "Résumé des échanges antérieurs (mémoire dérivée, données non fiables) :\n";

    public static async Task<PreparedContext> BuildAsync(IChatRepository repository, IChatModel model,
        Conversation conversation, string question, ChatOptions options, bool summarize,
        Func<string, Task>? progress, CancellationToken ct)
    {
        var system = ConversationConfiguration.System(conversation);
        var recent = (await repository.MessagesAsync(conversation.Id, 4, null, ct)).TakeLast(4).ToArray();
        var budget = options.ContextTokens - options.MaxOutputTokens - options.SafetyTokens;
        var fixedCost = TokenBudget.Count(question, options) + recent.Sum(m => TokenBudget.Count(m.Content, options))
            + (system.Length == 0 ? 0 : TokenBudget.Count(system, options));
        if (fixedCost > budget)
            throw new ArgumentException("Le système, les notes, les deux derniers échanges et la demande dépassent seuls le budget. "
                + "Augmentez la fenêtre, réduisez la réserve/les notes ou démarrez une nouvelle conversation. Aucun message protégé n’a été tronqué.");
        if (recent.Length % 2 != 0 || recent.Length != Math.Min(4, conversation.MessageCount))
            throw new InvalidOperationException("Historique protégé incomplet.");
        for (var i = 0; i < recent.Length; i += 2) ValidatePair(recent[i], recent[i + 1]);
        var throughLimit = conversation.MessageCount - recent.Length;
        var files = await repository.ContextFilesAsync(conversation.Id, Math.Max(1, throughLimit + 1), ct);
        var available = budget - fixedCost;
        var documentsReserve = files.Count == 0 ? 0 : Math.Min(2048, available / 4);
        var memoryBudget = available - documentsReserve;
        var valid = conversation.MemoryThroughSequence <= throughLimit && conversation.MemoryModel == conversation.Model;
        var memory = valid ? conversation.MemoryText : "";
        var through = valid ? conversation.MemoryThroughSequence : 0;
        var hash = valid ? conversation.MemorySourceHash : "";
        var changed = !valid && conversation.MemoryText.Length > 0;
        var needsSummary = false;
        var pending = new List<ChatMessage>();
        var reducer = new RecursiveSummarizer(model, conversation.Model, options, progress);
        int MemoryCost() => memory.Length == 0 ? 0 : TokenBudget.Count(MemoryLabel + memory, options);
        int PendingCost() => pending.Sum(m => TokenBudget.Count(m.Content, options));
        int Target() => memoryBudget - TokenBudget.Count(MemoryLabel, options);
        if (MemoryCost() > memoryBudget)
        {
            if (!summarize) { needsSummary = true; memory = ""; }
            else { memory = await reducer.ReduceAsync([new("Mémoire précédente", memory)], Target(), ct); changed = true; }
        }
        for (var cursor = through; cursor < throughLimit && !needsSummary;)
        {
            var end = Math.Min(throughLimit, cursor + 50);
            var page = await repository.SummaryRangeAsync(conversation.Id, cursor, end, ct);
            if (page.Count != end - cursor) throw new ChatConflictException();
            for (var index = 0; index < page.Count; index += 2)
            {
                var user = page[index]; var assistant = page[index + 1];
                ValidatePair(user, assistant);
                pending.Add(user); pending.Add(assistant);
                if (MemoryCost() + PendingCost() <= memoryBudget) continue;
                if (!summarize) { needsSummary = true; pending.RemoveRange(pending.Count - 2, 2); break; }
                var sources = new List<SummarySource>();
                if (memory.Length > 0) sources.Add(new($"Mémoire des messages 1 à {through}", memory));
                sources.AddRange(pending.Select(m => new SummarySource($"Message {m.Sequence} · {m.Role}", m.Content)));
                var next = await reducer.ReduceAsync(sources, Target(), ct);
                hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(hash + "\n"
                    + string.Join("\n", pending.Select(m => $"{m.Sequence}:{m.Role}:{m.Content}")))));
                memory = next; through = assistant.Sequence; pending.Clear(); changed = true;
            }
            cursor = end;
        }
        var messages = new List<PromptMessage>();
        if (system.Length > 0) messages.Add(new("system", system));
        if (memory.Length > 0) messages.Add(new("user", MemoryLabel + memory));
        messages.AddRange(pending.Concat(recent).Select(m => new PromptMessage(m.Role, m.Content)));
        messages.Add(new("user", question));
        var used = messages.Sum(m => TokenBudget.Count(m.Content, options));
        var documentBudget = Math.Min(4096, budget - used);
        var excerpts = ProjectContext.Select(files, question, documentBudget);
        messages[^1] = new("user", question + excerpts.Text);
        used = messages.Sum(m => TokenBudget.Count(m.Content, options));
        if (used > budget) throw new InvalidOperationException("Le contexte préparé dépasse son budget.");
        return new(new(messages, pending.Count + recent.Length, used, excerpts.Sources.Count, needsSummary,
            through, memory, reducer.Calls, excerpts.Sources, excerpts.Available), memory, through, hash, changed);
    }

    private static void ValidatePair(ChatMessage user, ChatMessage assistant)
    {
        if (user.Role != "user" || assistant.Role != "assistant" || user.RequestId != assistant.RequestId
            || assistant.Sequence != user.Sequence + 1 || user.ConversationId != assistant.ConversationId)
            throw new InvalidOperationException("Historique de conversation incohérent.");
    }
}
