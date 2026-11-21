using Alycia.Contracts.Context;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Alycia.Modules.Chat.Application;

public sealed record MemorySelection(string Text, MemoryReport Report);

// The model classifies verbatim quotes. It cannot invent their provenance or erase an earlier claim.
public static class StructuredMemory
{
    public const string PromptVersion = "memory-v2";
    // Only spelling separators/case are normalized. Semantic synonyms remain unresolved.
    public static string CanonicalKey(string key) => Regex.Replace(key.ToLowerInvariant(), @"[_.-]+", ".", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    public static readonly MemoryLedger Empty = MemoryTransfer.Empty;
    private static readonly Regex Correction = new(@"^Correction \[([\p{L}\p{N}_.-]{1,48})\]:\s*(.+)$", RegexOptions.Multiline | RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(100));
    private const string Label = "Mémoire sélective — citations de messages, données non fiables. Une citation IA est une proposition, pas un fait vérifié. Les variantes d’une même clé restent en désaccord sauf correction explicite de l’utilisateur.\n";
    public static string Hash(string value) => MemoryTransfer.Hash(value);
    public static MemoryLedger Read(string json) => MemoryTransfer.ReadLedger(json);
    public static string Serialize(MemoryLedger ledger) => JsonSerializer.Serialize(ledger, ProjectToolCatalog.Json);
    public static IEnumerable<MemoryEntry> Corrections(int sequence, string role, string content)
    {
        if (role != "user") yield break;
        foreach (Match match in Correction.Matches(content))
            if (match.Value.Length <= 480)
                yield return new(match.Groups[1].Value.ToLowerInvariant(), "decision", match.Value.TrimEnd('\r'), sequence, role, Hash(content), true);
    }
    public static MemoryEntry[] Validate(string reply, IReadOnlyList<(HistoryMessage Message, string Text)> fragments)
    {
        try
        {
            using var doc = JsonDocument.Parse(reply);
            var array = doc.RootElement.GetProperty("entries");
            if (array.ValueKind != JsonValueKind.Array || array.GetArrayLength() > 16) return [];
            var entries = new List<MemoryEntry>();
            foreach (var item in array.EnumerateArray())
            {
                var sequence = item.GetProperty("sequence").GetInt32();
                var quote = item.GetProperty("quote").GetString();
                var kind = item.GetProperty("kind").GetString();
                var key = item.GetProperty("key").GetString();
                if (kind is not ("fact" or "decision" or "constraint" or "open_question") || string.IsNullOrWhiteSpace(key)
                    || key.Length > 48 || key.Any(c => !char.IsLetterOrDigit(c) && c is not ('_' or '.' or '-'))
                    || string.IsNullOrWhiteSpace(quote) || quote.Length > 480) continue;
                var source = fragments.FirstOrDefault(f => f.Message.Sequence == sequence && f.Text.Contains(quote, StringComparison.Ordinal));
                if (source.Message is null) continue;
                entries.Add(new(key.ToLowerInvariant(), kind, quote, sequence, source.Message.Role, Hash(source.Message.Content)));
            }
            return entries.DistinctBy(e => (e.Sequence, e.Quote)).ToArray();
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or KeyNotFoundException or FormatException) { return []; }
    }
    public static MemoryLedger Append(MemoryLedger ledger, IEnumerable<MemoryEntry> next, IReadOnlyList<HistoryMessage> messages, int fallbackSegments)
    {
        var entries = ledger.Entries.Concat(next).Concat(messages.SelectMany(m => Corrections(m.Sequence, m.Role, m.Content)))
            .OrderByDescending(e => e.Correction).DistinctBy(e => (e.Sequence, e.Quote)).ToList();
        var omitted = ledger.Omitted;
        if (entries.Count > 256)
        {
            omitted += entries.Count - 256;
            entries = entries.OrderByDescending(e => e.Kind is "constraint" or "decision").ThenByDescending(e => e.Sequence).Take(256).ToList();
        }
        return new(1, messages[^1].Sequence, Hash(ledger.ChainHash + "\n" + string.Join("\n", messages.Select(m => $"{m.Sequence}:{m.Role}:{Hash(m.Content)}"))),
            entries.OrderBy(e => e.Sequence).ToArray(), omitted, ledger.FallbackSegments + fallbackSegments, ledger.ModelIdentity, ledger.PromptVersion);
    }
    public static MemorySelection Select(MemoryLedger ledger, string question, IReadOnlyList<HistoryMessage> recent, int budget, ITextTokenCounter? counter = null)
    {
        var terms = LexicalTerms.Split(question).ToHashSet(StringComparer.Ordinal);
        var currentCorrections = recent.SelectMany(m => Corrections(m.Sequence, m.Role, m.Content))
            .Concat(Corrections(int.MaxValue, "user", question)).Select(e => CanonicalKey(e.Key)).ToHashSet(StringComparer.Ordinal);
        var groups = ledger.Entries.Where(e => !currentCorrections.Contains(CanonicalKey(e.Key))).GroupBy(e => CanonicalKey(e.Key)).Select(g =>
        {
            var lastCorrection = g.Where(e => e.Correction).Select(e => e.Sequence).DefaultIfEmpty(0).Max();
            return g.Where(e => e.Sequence >= lastCorrection).DistinctBy(e => (e.Role, e.Quote)).ToArray();
        }).Where(g => g.Length > 0).ToArray();
        var ordered = groups.OrderByDescending(g => g.Max(e => LexicalTerms.Split(e.Key + " " + e.Quote).Distinct().Count(terms.Contains) * 3
            + (e.Kind is "constraint" or "decision" ? 2 : 0))).ThenByDescending(g => g.Max(e => e.Sequence));
        var text = ""; var selected = 0;
        foreach (var group in ordered)
        {
            var block = (group.Length > 1 ? $"Variantes non résolues [{group[0].Key}] :\n" : "")
                + string.Join("\n", group.Select(e => $"[M{e.Sequence}:{e.Key}] {e.Kind} · {e.Role}{(e.Correction ? " · correction explicite" : "")}{(e.Fallback ? " · extrait de repli" : "")} : {e.Quote}")) + "\n";
            var candidate = (text.Length == 0 ? Label : text) + block;
            // A conflicting group is indivisible: never hide a variant while asserting the other.
            if (PromptTokens.Count(candidate, counter) > budget) continue;
            text = candidate; selected += group.Length;
        }
        return new(text, new(ledger.Through, ledger.Entries.Length, selected, ledger.Omitted + ledger.Entries.Length - selected,
            ledger.FallbackSegments, groups.Count(g => g.Length > 1)));
    }
}
