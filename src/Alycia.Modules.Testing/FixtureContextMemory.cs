using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Alycia.Contracts.Context;
using Alycia.Contracts.Memory;

namespace Alycia.Modules.Testing;

public sealed class FixtureContextMemory(DateTimeOffset clock = default) : IContextMemory
{
    public string PromptVersion => "memory-v2";
    public MemoryEntry[] Validate(string response, IReadOnlyList<(HistoryMessage Message, string Text)> fragments)
    {
        try
        {
            using var json = JsonDocument.Parse(response);
            if (!json.RootElement.TryGetProperty("entries", out var data) || data.ValueKind != JsonValueKind.Array || data.GetArrayLength() > 16) return [];
            var result = new List<MemoryEntry>();
            foreach (var item in data.EnumerateArray())
            {
                var key = item.GetProperty("key").GetString(); var quote = item.GetProperty("quote").GetString();
                var kind = item.GetProperty("kind").GetString(); var seq = item.GetProperty("sequence").GetInt32();
                if (key is null || !Regex.IsMatch(key, @"^[\p{L}\p{N}_.-]{1,48}$") || string.IsNullOrWhiteSpace(quote) || quote.Length > 480
                    || kind is not ("fact" or "decision" or "constraint" or "open_question")) continue;
                var source = fragments.FirstOrDefault(m => m.Message.Sequence == seq && m.Text.Contains(quote, StringComparison.Ordinal)).Message;
                if (source is not null) result.Add(new(key.ToLowerInvariant(), kind, quote, seq, source.Role, MemoryTransfer.Hash(source.Content)));
            }
            return result.DistinctBy(x => (x.Sequence, x.Quote)).ToArray();
        }
        catch (Exception e) when (e is JsonException or FormatException or InvalidOperationException or KeyNotFoundException) { return []; }
    }
    public MemoryLedger Append(MemoryLedger ledger, IEnumerable<MemoryEntry> entries, IReadOnlyList<HistoryMessage> messages, int fallback) =>
        ledger with
        {
            Through = messages[^1].Sequence,
            ChainHash = MemoryTransfer.Hash(ledger.ChainHash + "\n" + string.Join("\n", messages.Select(m => $"{m.Sequence}:{m.Role}:{MemoryTransfer.Hash(m.Content)}"))),
            Entries = ledger.Entries.Concat(entries).DistinctBy(e => (e.Sequence, e.Quote)).OrderBy(e => e.Sequence).ToArray(),
            FallbackSegments = ledger.FallbackSegments + fallback
        };
    public MemorySelection Select(MemoryLedger ledger, string question, IReadOnlyList<HistoryMessage> recent, int budget, ITextTokenCounter? counter)
    {
        var hidden = PendingCorrections(recent, int.MaxValue, question).Select(f => f.Key).ToHashSet();
        var rows = ledger.Entries.Where(e => !hidden.Contains(e.Key)).ToArray();
        var text = string.Join("\n", rows.Select(e => e.Quote));
        var selected = PromptTokens.Count(text, counter) <= budget ? rows.Length : 0;
        return new(selected == 0 ? "" : text, new(ledger.Through, ledger.Entries.Length, selected, ledger.Omitted + ledger.Entries.Length - selected,
            ledger.FallbackSegments, rows.GroupBy(r => r.Key).Count(g => g.Select(x => x.Quote).Distinct().Count() > 1)));
    }
    public DateTimeOffset? AsOf(string question)
    {
        var match = Regex.Match(question, @"\b(au|as of|avant le|before)\s+(\d{4}-\d\d-\d\d)\b", RegexOptions.IgnoreCase);
        if (!DateOnly.TryParseExact(match.Groups[2].Value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)) return null;
        var result = new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
        return match.Groups[1].Value.ToLowerInvariant() is "before" or "avant le" ? result : date == DateOnly.MaxValue ? DateTimeOffset.MaxValue : result.AddDays(1);
    }
    public IEnumerable<TemporalFact> PendingCorrections(IReadOnlyList<HistoryMessage> recent, int sequence, string question) =>
        recent.Where(m => m.Role == "user").Select(m => (m.Sequence, m.Content, m.CreatedAtUtc)).Append((sequence, question, clock))
            .SelectMany(m => Regex.Matches(m.Item2, @"^Correction \[([\p{L}\p{N}_.-]+)\]:\s*.+$", RegexOptions.Multiline | RegexOptions.IgnoreCase)
            .Select(x => new TemporalFact(x.Groups[1].Value.ToLowerInvariant(), x.Value, "user", m.Item1, m.Item3, MemoryTransfer.Hash(m.Item2), x.Index, m.Item2.Length, true, "correction"))).ToArray();
}
