using Alycia.Contracts.Documents;
using Alycia.Contracts.Context;
using System.Text;

namespace Alycia.Modules.Chat.Application;

public static class FileContext
{
    public static ContextWindow Build(Alycia.Contracts.Context.ISlidingHistory policy, string system, IReadOnlyList<HistoryMessage> history, string question,
        ContextParameters options, IReadOnlyList<TurnFile> files, int currentSequence)
    {
        var original = SlidingContext.Build(policy, system, history, question, options);
        var originalStart = original.HistoryMessagesIncluded == 0 ? currentSequence
            : history[history.Count - original.HistoryMessagesIncluded].Sequence;
        if (!files.Any(f => f.InContextWindow(originalStart, currentSequence)))
            return original;

        var available = options.ContextTokens - options.MaxOutputTokens - options.SafetyTokens
            - options.Count(question) - (system.Length == 0 ? 0 : options.Count(system));
        if (available < 256 && !options.Evidence.EvidenceSelectionEnabled) return original;
        var reserve = EvidenceComposer.DocumentReserve(available, options, 2);
        // Use the original configured context but reserve part of its input budget for files.
        var reduced = options.WithSafetyTokens(options.SafetyTokens + reserve);
        var window = SlidingContext.Build(policy, system, history, question, reduced);
        var earliest = window.HistoryMessagesIncluded == 0 ? currentSequence
            : history[history.Count - window.HistoryMessagesIncluded].Sequence;
        var excerpts = ProjectContext.Select(files.Where(f => f.InContextWindow(earliest, currentSequence)), question, reserve, options);
        if (excerpts.Sources.Count == 0) return original with { FilesAvailable = excerpts.Available, Assembly = excerpts.Assembly };
        var messages = window.Messages.ToList();
        messages[^1] = messages[^1] with { Content = question + excerpts.Text };
        return new(messages, window.HistoryMessagesIncluded,
            messages.Sum(m => options.Count(m.Content)), excerpts.Sources.Count,
            Sources: excerpts.Sources, FilesAvailable: excerpts.Available, Retrieval: options.Evidence.Sources.Report, Assembly: excerpts.Assembly);
    }

    public static string Utf8Prefix(string text, int bytes) => Alycia.Contracts.Documents.Utf8Text.Prefix(text, bytes);
}
