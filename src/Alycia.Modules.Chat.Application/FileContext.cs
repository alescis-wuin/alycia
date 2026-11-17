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
            - SlidingContext.EstimateTokens(question) - (system.Length == 0 ? 0 : SlidingContext.EstimateTokens(system));
        if (available < 256) return original;
        var reserve = Math.Min(available / 2, 4096);
        // Use the original configured context but reserve part of its input budget for files.
        var reduced = new ChatOptions
        {
            ContextTokens = options.ContextTokens,
            MaxOutputTokens = options.MaxOutputTokens,
            SafetyTokens = options.SafetyTokens + reserve,
            MaxHistoryTurns = options.MaxHistoryTurns,
            TimeoutSeconds = options.TimeoutSeconds,
            Temperature = options.Temperature,
            Seed = options.Seed
        };
        var window = SlidingContext.Build(system, history, question, reduced);
        var earliest = window.HistoryMessagesIncluded == 0 ? currentSequence
            : history[history.Count - window.HistoryMessagesIncluded].Sequence;
        var excerpts = ProjectContext.Select(files.Where(f => ProjectContext.Eligible(f, earliest, currentSequence)), question, reserve);
        if (excerpts.Sources.Count == 0) return original with { FilesAvailable = excerpts.Available };
        var messages = window.Messages.ToList();
        messages[^1] = messages[^1] with { Content = question + excerpts.Text };
        return new(messages, window.HistoryMessagesIncluded,
            window.EstimatedInputTokens + Encoding.UTF8.GetByteCount(excerpts.Text), excerpts.Sources.Count,
            Sources: excerpts.Sources, FilesAvailable: excerpts.Available);
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
