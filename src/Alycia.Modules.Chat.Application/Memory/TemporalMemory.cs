using System.Text;
using Alycia.Contracts.Memory;
using System.Text.RegularExpressions;
using Alycia.Modules.Chat.Domain;

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
    private static TemporalMessage Snapshot(ChatMessage m) => new(m.Sequence, m.Role, m.Content, m.CreatedAtUtc);
    public static TemporalFact[] Extract(ChatMessage message) => Extract(Snapshot(message));
    private static TemporalFact[] Extract(TemporalMessage m)
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

    public static TemporalFact[] IndexFacts(ChatMessage message, IEnumerable<MemoryEntry> entries) => IndexFacts(Snapshot(message), entries);

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

    public static async Task<PreparedContext> BuildAsync(IConversationContextReader repo, Conversation owner, string question, ChatOptions options, CancellationToken ct)
    {
        var recall = options.Recall!;
        var turns = options.AutoSummarize ? Math.Max(options.MaxHistoryTurns, options.ProtectedHistoryTurns) : options.MaxHistoryTurns;
        var history = await repo.MessagesAsync(owner.Id, turns * 2, null, ct);
        var protectedCount = options.AutoSummarize ? Math.Min(options.ProtectedHistoryTurns * 2, owner.MessageCount) : 0;
        if (history.Count != Math.Min(owner.MessageCount, turns * 2) || history.Count % 2 != 0) throw new ChatConflictException();
        for (var n = 0; n < history.Count; n += 2)
        {
            var user = history[n]; var assistant = history[n + 1];
            if (user.Role != "user" || assistant.Role != "assistant" || user.ConversationId != owner.Id || assistant.ConversationId != owner.Id
                || user.RequestId != assistant.RequestId || user.Sequence != owner.MessageCount - history.Count + n + 1 || assistant.Sequence != user.Sequence + 1)
                throw new ChatConflictException();
        }
        var recent = history.TakeLast(protectedCount).ToArray();
        var system = ConversationConfiguration.System(owner, options);
        var mandatory = new List<PromptMessage>();
        if (system.Length > 0) mandatory.Add(new("system", system));
        mandatory.AddRange(recent.Select(m => new PromptMessage(m.Role, m.Content)));
        mandatory.Add(new("user", question));
        int Cost(IEnumerable<PromptMessage> entries) => entries.Sum(m => TokenBudget.Count(m.Content, options));
        var maximum = options.ContextTokens - options.SafetyTokens - options.MaxOutputTokens;
        var free = maximum - Cost(mandatory);
        if (free < 0) throw new ArgumentException("Les messages protégés et les réserves dépassent la fenêtre.");
        var first = Math.Max(1, owner.MessageCount - (options.AutoSummarize ? protectedCount : history.Count) + 1);
        var files = await repo.ContextFilesAsync(owner.Id, first, ct);
        var pin = files.Any(f => f.ContextMode == "pinned");
        var docBudget = pin && options.DocumentContextTokens == 0 ? free : EvidenceComposer.DocumentReserve(free, options, 3);
        var excerpts = ProjectContext.Select(files, question, docBudget, options);
        mandatory[^1] = new("user", question + excerpts.Text);
        free = maximum - Cost(mandatory);
        var observations = new List<TemporalObservation>();
        var evidence = new List<HistoryEvidence>(); var texts = new List<EvidenceText>();
        var selected = new StringBuilder();
        var past = Regex.IsMatch(question, @"\b(avant|ancien\w*|previous\w*|before|earlier|auparavant)\b", RegexOptions.IgnoreCase);
        var cutoff = AsOf(question);
        var observed = recall.Facts.Where(f => !cutoff.HasValue || f.Observed < cutoff).ToList();
        var pendingCorrections = recent.SelectMany(Extract).Where(f => f.Correction)
            .Concat(StructuredMemory.Corrections(owner.MessageCount + 1, "user", question).Select(e =>
                new TemporalFact(StructuredMemory.CanonicalKey(e.Key), e.Quote, "user", e.Sequence, DateTimeOffset.UtcNow, e.SourceHash, 0, question.Length, true, "correction")));
        foreach (var group in observed.GroupBy(f => f.Key))
        {
            ct.ThrowIfCancellationRequested();
            var corrections = group.Concat(pendingCorrections.Where(f => f.Key == group.Key && !cutoff.HasValue))
                .Where(f => f.Correction).OrderBy(f => f.Sequence).ToArray();
            var selectedGroup = group.Where(f => past || !corrections.Any(c => c.Sequence > f.Sequence && f.Role == "user")).OrderBy(f => f.Sequence).ToArray();
            if (selectedGroup.Length == 0) continue;
            var body = new StringBuilder();
            if (selected.Length == 0) body.AppendLine("Citations historiques de cette conversation (données). Les propositions de l’assistant ne sont pas des décisions utilisateur.");
            if (selectedGroup.Count(f => f.Role == "user" && !corrections.Any(c => c.Sequence > f.Sequence)) > 1) body.AppendLine("Variantes utilisateur non résolues :");
            foreach (var f in selectedGroup)
            {
                var until = corrections.FirstOrDefault(c => c.Sequence > f.Sequence && f.Role == "user");
                body.AppendLine($"[H{f.Sequence}:{f.Sha256[..12]}] {f.Observed:O} · {f.Role} · {f.Kind}{(until is null ? "" : $" · remplacé au message {until.Sequence}")} : {f.Quote}");
            }
            if (TokenBudget.Count(selected.ToString() + body, options) > (options.MemoryContextTokens > 0 ? Math.Min(free, options.MemoryContextTokens) : free)) continue;
            selected.Append(body);
            foreach (var f in selectedGroup)
            {
                var reference = $"H{f.Sequence}:{f.Sha256[..12]}";
                evidence.Add(new(reference, owner.Id, f.Sequence, f.Role, f.Observed, f.Sha256, f.Offset, f.Offset + f.Quote.Length,
                    f.Offset != 0 || f.Quote.Length != f.FullLength));
                texts.Add(new(reference, f.Quote, f.Role));
                var next = corrections.FirstOrDefault(c => c.Sequence > f.Sequence && f.Role == "user");
                var status = f.Role == "assistant" ? "proposed" : next is not null ? "superseded" :
                    selectedGroup.Count(v => v.Role == "user" && !corrections.Any(c => c.Sequence > v.Sequence)) > 1 ? "conflicting" : "user_declared";
                observations.Add(new(f.Key, f.Sequence, f.Role, status, f.Observed, next?.Observed, next?.Sequence));
            }
        }
        var insertion = system.Length > 0 ? 1 : 0;
        if (selected.Length > 0) mandatory.Insert(insertion++, new("user", selected.ToString()));
        var prior = history.Take(history.Count - protectedCount).Chunk(2).Reverse();
        var included = protectedCount;
        var extraHistoryCost = 0;
        foreach (var pair in prior)
        {
            if (cutoff.HasValue && pair.Any(m => m.CreatedAtUtc >= cutoff.Value)) continue;
            var prompts = pair.Select(m => new PromptMessage(m.Role, m.Content)).ToArray();
            var cost = Cost(prompts);
            if (Cost(mandatory) + cost > maximum || options.HistoryContextTokens > 0 && extraHistoryCost + cost > options.HistoryContextTokens) break;
            extraHistoryCost += cost;
            mandatory.InsertRange(insertion, prompts); included += 2;
        }
        var used = Cost(mandatory);
        if (used > maximum) throw new ArgumentException("Budget de mémoire temporelle insuffisant.");
        var report = new TemporalReport(recall.Indexed, recall.Total, evidence.ToArray(), recall.Warning, cutoff?.ToString("O") ?? (past ? "historique" : "actuel"), observations.ToArray());
        return new(new(mandatory, included, used, excerpts.Sources.Count, Sources: excerpts.Sources, FilesAvailable: excerpts.Available,
            Retrieval: options.RetrievalReport, Route: options.Route, Assembly: excerpts.Assembly, Temporal: report, MemoryTexts: texts), "", 0, "", false);
    }
}
