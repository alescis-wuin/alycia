using Alycia.Contracts.Context;
using System.Text;
using System.Text.RegularExpressions;
using Alycia.Contracts.Memory;

namespace Alycia.Modules.Chat.Application;

public static class TemporalContext
{
    public static async Task<PreparedContext> BuildAsync(IConversationContextReader repo, ContextConversation owner, string question, ContextParameters options, CancellationToken ct)
    {
        var recall = options.Evidence.Sources.Recall!;
        var turns = options.History.AutoSummarize ? Math.Max(options.History.MaxHistoryTurns, options.History.ProtectedHistoryTurns) : options.History.MaxHistoryTurns;
        var history = await repo.RecentAsync(owner.Id, turns * 2, null, ct);
        var protectedCount = options.History.AutoSummarize ? Math.Min(options.History.ProtectedHistoryTurns * 2, owner.MessageCount) : 0;
        if (history.Count != Math.Min(owner.MessageCount, turns * 2) || history.Count % 2 != 0) throw new ChatConflictException();
        for (var n = 0; n < history.Count; n += 2)
        {
            var user = history[n]; var assistant = history[n + 1];
            if (user.Role != "user" || assistant.Role != "assistant" || user.ConversationId != owner.Id || assistant.ConversationId != owner.Id
                || user.RequestId != assistant.RequestId || user.Sequence != owner.MessageCount - history.Count + n + 1 || assistant.Sequence != user.Sequence + 1)
                throw new ChatConflictException();
        }
        var recent = history.TakeLast(protectedCount).ToArray();
        var system = owner.SystemText;
        var mandatory = new List<PromptMessage>();
        if (system.Length > 0) mandatory.Add(new("system", system));
        mandatory.AddRange(recent.Select(m => new PromptMessage(m.Role, m.Content)));
        mandatory.Add(new("user", question));
        int Cost(IEnumerable<PromptMessage> entries) => entries.Sum(m => options.Count(m.Content));
        var maximum = options.ContextTokens - options.SafetyTokens - options.MaxOutputTokens;
        var free = maximum - Cost(mandatory);
        if (free < 0) throw new ArgumentException("Les messages protégés et les réserves dépassent la fenêtre.");
        var first = Math.Max(1, owner.MessageCount - (options.History.AutoSummarize ? protectedCount : history.Count) + 1);
        var files = await repo.ContextFilesAsync(owner.Id, first, ct);
        var pin = files.Any(f => f.ContextMode == "pinned");
        var docBudget = pin && options.Evidence.DocumentContextTokens == 0 ? free : EvidenceComposer.DocumentReserve(free, options, 3);
        var excerpts = ProjectContext.Select(files, question, docBudget, options);
        mandatory[^1] = new("user", question + excerpts.Text);
        free = maximum - Cost(mandatory);
        var observations = new List<TemporalObservation>();
        var evidence = new List<HistoryEvidence>(); var texts = new List<EvidenceText>();
        var selected = new StringBuilder();
        var past = Regex.IsMatch(question, @"\b(avant|ancien\w*|previous\w*|before|earlier|auparavant)\b", RegexOptions.IgnoreCase);
        var cutoff = TemporalMemory.AsOf(question);
        var observed = recall.Facts.Where(f => !cutoff.HasValue || f.Observed < cutoff).ToList();
        var pendingCorrections = recent.SelectMany(m => TemporalMemory.Extract(new TemporalMessage(m.Sequence, m.Role, m.Content, m.CreatedAtUtc))).Where(f => f.Correction)
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
            if (options.Count(selected.ToString() + body) > (options.History.Budget.Memory > 0 ? Math.Min(free, options.History.Budget.Memory) : free)) continue;
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
            if (Cost(mandatory) + cost > maximum || options.History.Budget.History > 0 && extraHistoryCost + cost > options.History.Budget.History) break;
            extraHistoryCost += cost;
            mandatory.InsertRange(insertion, prompts); included += 2;
        }
        var used = Cost(mandatory);
        if (used > maximum) throw new ArgumentException("Budget de mémoire temporelle insuffisant.");
        var report = new TemporalReport(recall.Indexed, recall.Total, evidence.ToArray(), recall.Warning, cutoff?.ToString("O") ?? (past ? "historique" : "actuel"), observations.ToArray());
        return new(new(mandatory, included, used, excerpts.Sources.Count, Sources: excerpts.Sources, FilesAvailable: excerpts.Available,
            Retrieval: options.Evidence.Sources.Report, Route: options.Evidence.Sources.Query.Route, Assembly: excerpts.Assembly, Temporal: report, MemoryTexts: texts), "", 0, "", false);
    }
}
