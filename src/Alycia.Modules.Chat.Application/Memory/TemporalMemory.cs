using Alycia.Contracts.Memory;
using System.Text.RegularExpressions;

namespace Alycia.Modules.Chat.Application;

public sealed record TemporalFact(string Key, string Quote, string Role, int Sequence, DateTimeOffset Observed, string Sha256,
    int Offset, int FullLength, bool Correction, string Kind);
public sealed record TemporalRecall(TemporalFact[] Facts, int Indexed, int Total, IReadOnlyList<ModelCallUsage> Calls, string? Warning = null);
public interface ITemporalMemory
{
    Task<TemporalRecall> RecallAsync(Guid owner, string question, ChatOptions settings, int through, CancellationToken ct);
}

public static class TemporalMemory
{
    private static readonly Regex Topic = new(@"(?:^|\n)(?:Décision|Decision|Contrainte|Constraint|Fait|Fact|Proposition|Correction)\s*\[([\p{L}\p{N}_.-]{1,48})\]\s*:", RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(100));
    public static TemporalFact[] Extract(TemporalMessage m)
    {
        var anchors = Topic.Matches(m.Content).Cast<Match>().Take(16).ToArray();
        var hash = StructuredMemory.Hash(m.Content);
        TemporalFact Fact(int at, int length, string key, bool correction, string kind)
        {
            var quote = FileContext.Utf8Prefix(m.Content.Substring(at, length), 1024);
            return new(key, quote, m.Role, m.Sequence, m.CreatedAtUtc, hash, at, m.Content.Length,
                correction && m.Role == "user", m.Role == "assistant" ? "proposition" : kind);
        }
        if (anchors.Length == 0) return [Fact(0, Math.Min(m.Content.Length, 1024), "message-" + m.Sequence, false, "déclaration")];
        return anchors.Select((p, n) =>
        {
            var at = p.Index + (m.Content[p.Index] == '\n' ? 1 : 0);
            var length = (n + 1 < anchors.Length ? anchors[n + 1].Index : m.Content.Length) - at;
            var kind = p.Value.Trim().Split(' ', '[')[0].ToLowerInvariant();
            return Fact(at, Math.Min(1024, length), StructuredMemory.CanonicalKey(p.Groups[1].Value), kind == "correction", kind);
        }).ToArray();
    }

    public static TemporalFact[] IndexFacts(TemporalMessage message, IEnumerable<MemoryEntry> entries)
    {
        var extracted = Extract(message);
        var hash = extracted[0].Sha256;
        var validated = entries.Where(e => e.Sequence == message.Sequence && e.Role == message.Role && e.SourceHash == hash && !e.Fallback)
            .Select(e => (Entry: e, Offset: message.Content.IndexOf(e.Quote, StringComparison.Ordinal)))
            .Where(x => x.Offset >= 0)
            .Select(x => new TemporalFact(StructuredMemory.CanonicalKey(x.Entry.Key), x.Entry.Quote, message.Role, message.Sequence,
                message.CreatedAtUtc, hash, x.Offset, message.Content.Length, x.Entry.Correction && message.Role == "user",
                message.Role == "assistant" ? "proposition" : x.Entry.Kind));
        return extracted.Concat(validated).DistinctBy(f => (f.Key, f.Quote)).ToArray();
    }

    public static DateTimeOffset? AsOf(string question)
    {
        var match = Regex.Match(question, @"\b(?:au|avant le|as of|before)\s+(\d{4}-\d{2}-\d{2})\b", RegexOptions.IgnoreCase);
        if (!DateOnly.TryParseExact(match.Groups[1].Value, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var date)) return null;
        var at = new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
        return match.Value.StartsWith("avant", StringComparison.OrdinalIgnoreCase) || match.Value.StartsWith("before", StringComparison.OrdinalIgnoreCase)
            ? at : date == DateOnly.MaxValue ? DateTimeOffset.MaxValue : at.AddDays(1);
    }
}
