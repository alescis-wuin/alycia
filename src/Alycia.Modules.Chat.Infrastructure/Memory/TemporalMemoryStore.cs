using System.Text.Json;
using Alycia.Modules.Chat.Application;
using Alycia.Contracts.Memory;

namespace Alycia.Modules.Chat.Infrastructure;

public sealed class TemporalMemoryStore(ITemporalFactStorage storage, ITemporalHistorySources sources, IIndexedEmbeddings vectors, IHistoryKnowledge? knowledge = null)
    : ITemporalMemory, ITemporalMemoryIndex
{
    public async Task RefreshAsync(CancellationToken ct)
    {
        Guid? cursor = null;
        while (true)
        {
            var owners = await sources.OwnersAsync(cursor, 64, ct);
            foreach (var owner in owners)
            {
                var missing = new List<int>();
                for (var after = await storage.ContiguousThroughAsync(owner.Id, owner.Through, ct); after < owner.Through && missing.Count < 32;)
                {
                    var page = await sources.SequencesAsync(owner.Id, after, owner.Through, ct);
                    if (page.Length == 0) break;
                    var indexed = await storage.IndexedAsync(owner.Id, page, ct);
                    missing.AddRange(page.Except(indexed).Take(32 - missing.Count));
                    after = page[^1];
                    if (after >= owner.Through) break;
                }
                if (missing.Count == 0) continue;
                var ledger = MemoryTransfer.ReadLedger(owner.LedgerJson).Entries.ToLookup(f => f.Sequence);
                foreach (var message in await sources.MessagesAsync(owner.Id, missing.ToArray(), ct))
                {
                    var facts = TemporalMemory.IndexFacts(message, ledger[message.Sequence]);
                    await storage.SaveAsync(owner.Id, message.Sequence, message.CreatedAtUtc, facts.Select(f => f.Key).Distinct().ToArray(),
                        JsonSerializer.Serialize(facts, ChatJson.Options), string.Join(" ", facts.Select(f => f.Key + " " + f.Quote)), ct);
                }
            }
            if (owners.Length < 64) return;
            cursor = owners[^1].Id;
        }
    }

    public async Task<TemporalRecall> RecallAsync(Guid owner, string question, ChatOptions settings, int through, CancellationToken ct)
    {
        settings = settings.Copy();
        var before = TemporalMemory.AsOf(question) ?? DateTimeOffset.MaxValue;
        var indexed = await storage.CountAsync(owner, through, ct);
        var query = string.Join(" OR ", ProjectRetrieval.Tokens(question).Distinct().Take(24));
        var candidates = await storage.LexicalAsync(owner, through, before, query, ct);
        MemoryVectorMatches matches = new(new Dictionary<int, double>(), []);
        if (indexed > 0 && settings.RetrievalMode == "hybrid" && settings.EmbedHistory && settings.EmbeddingModel.Length > 0)
        {
            matches = await vectors.SearchMemoryAsync(owner, question, settings, through, before, ct);
            var ids = matches.Scores.Keys.ToArray();
            if (ids.Length > 0)
                candidates.AddRange(await storage.SelectedAsync(owner, through, before, ids, ct));        }
        var groupScores = settings.Enrichment.Enabled && settings.EmbedHistory && knowledge is not null
            ? await knowledge.HistoryAsync(owner, question, settings.Enrichment.Profile, through, before, ct)
            : new Dictionary<int, double>();
        if (groupScores.Count > 0)
        {
            var sequences = groupScores.Keys.ToArray();
            candidates.AddRange(await storage.SelectedAsync(owner, through, before, sequences, ct));        }
        var facts = Decode(candidates).Distinct().ToArray();
        if (facts.Length == 0) return new([], indexed, through, matches.Calls, matches.Warning);
        var semantic = matches.Scores; var calls = matches.Calls; var warning = matches.Warning;
        var chunks = facts.Select(f => new SourceChunk(owner, "", f.Sha256, f.Key, f.Kind, 0, f.Quote.Length, 1, 1, f.Quote, [], true)).ToArray();
        var ranked = ProjectRetrieval.Bm25(chunks, question);
        var terms = Enumerable.Range(0, facts.Length).OrderByDescending(n => ranked[n] + groupScores.GetValueOrDefault(facts[n].Sequence) + 2 * Math.Max(0, semantic.GetValueOrDefault(facts[n].Sequence)))
            .ThenByDescending(n => facts[n].Sequence)
            .Where(n => ranked[n] > 0 || groupScores.ContainsKey(facts[n].Sequence) || semantic.GetValueOrDefault(facts[n].Sequence) > .25)
            .Select(n => facts[n].Key).Distinct().Take(8).ToArray();
        if (terms.Length == 0) return new([], indexed, through, calls, warning);
        var variants = await storage.TopicsAsync(owner, through, before, terms, ct);
        if (variants.Count > 256) return new([], indexed, through, calls, "Trop de variantes pour restituer leurs relations sans omission.");
        return new(Decode(variants).Where(f => terms.Contains(f.Key)).OrderBy(f => Array.IndexOf(terms, f.Key)).ThenBy(f => f.Sequence).ToArray(),
            indexed, through, calls, warning);
    }

    public async Task<TemporalFact[]> ExportAsync(Guid owner, int after, int limit, CancellationToken ct) =>
        Decode(await storage.ExportAsync(owner, after, limit, ct));
    public Task<int> ClearAsync(Guid owner, CancellationToken ct) => storage.ClearAsync(owner, ct);

    private static TemporalFact[] Decode(IEnumerable<string> rows) => rows.SelectMany(row =>
        JsonSerializer.Deserialize<TemporalFact[]>(row, ChatJson.Options) ?? []).ToArray();
}
