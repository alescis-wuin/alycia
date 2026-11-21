using System.Text;
using Alycia.Modules.Chat.Domain;

namespace Alycia.Modules.Chat.Application;

public static class FileContext
{
    public static ContextWindow Build(string system, IReadOnlyList<ChatMessage> history, string question,
        ChatOptions options, IReadOnlyList<Attachment> files, int currentSequence)
    {
        var original = SlidingContext.Build(system, history, question, options);
        var originalStart = original.HistoryMessagesIncluded == 0 ? currentSequence
            : history[history.Count - original.HistoryMessagesIncluded].Sequence;
        if (!files.Any(f => ProjectContext.Eligible(f, originalStart, currentSequence)))
            return original;

        var available = options.ContextTokens - options.MaxOutputTokens - options.SafetyTokens
            - TokenBudget.Count(question, options) - (system.Length == 0 ? 0 : TokenBudget.Count(system, options));
        if (available < 256 && !options.EvidenceSelectionEnabled) return original;
        var reserve = EvidenceComposer.DocumentReserve(available, options, 2);
        // Use the original configured context but reserve part of its input budget for files.
        var reduced = options.Copy();
        reduced.SafetyTokens += reserve;
        var window = SlidingContext.Build(system, history, question, reduced);
        var earliest = window.HistoryMessagesIncluded == 0 ? currentSequence
            : history[history.Count - window.HistoryMessagesIncluded].Sequence;
        var excerpts = ProjectContext.Select(files.Where(f => ProjectContext.Eligible(f, earliest, currentSequence)), question, reserve, options);
        if (excerpts.Sources.Count == 0) return original with { FilesAvailable = excerpts.Available, Assembly = excerpts.Assembly };
        var messages = window.Messages.ToList();
        messages[^1] = messages[^1] with { Content = question + excerpts.Text };
        return new(messages, window.HistoryMessagesIncluded,
            messages.Sum(m => TokenBudget.Count(m.Content, options)), excerpts.Sources.Count,
            Sources: excerpts.Sources, FilesAvailable: excerpts.Available, Retrieval: options.RetrievalReport, Assembly: excerpts.Assembly);
    }

    public static string Utf8Prefix(string text, int bytes)
    {
        var result = new StringBuilder();
        foreach (var rune in text.EnumerateRunes())
        {
            if (rune.Utf8SequenceLength > bytes) break;
            result.Append(rune.ToString());
            bytes -= rune.Utf8SequenceLength;
        }
        return result.ToString();
    }
}
