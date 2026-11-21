using System.Text.Json;
using Alycia.Modules.Chat.Application;
using Microsoft.EntityFrameworkCore;

namespace Alycia.Modules.Chat.Infrastructure;

public sealed class TemporalMemoryStore(ChatDbContext database, IIndexedEmbeddings vectors, ChatOptions defaults, KnowledgeStore? knowledge = null)
    : ITemporalMemory, ITemporalMemoryIndex
{
    private const int Version = 1;

    // A row per immutable message is the checkpoint. The original remains authoritative.
    public async Task RefreshAsync(CancellationToken ct)
    {
        var owners = await database.Conversations.AsNoTracking().Where(c => c.MessageCount > 0).ToListAsync(ct);
        foreach (var owner in owners.Where(c => ConversationConfiguration.Options(c, defaults).TemporalMemoryEnabled))
        {
            var sequences = await database.Database.SqlQuery<int>($"""
                SELECT m."Sequence" AS "Value" FROM chat.messages m
                WHERE m."ConversationId" = {owner.Id} AND NOT EXISTS
                  (SELECT 1 FROM chat.temporal_memory t WHERE t.owner = m."ConversationId" AND t.sequence = m."Sequence" AND t.version = {Version})
                ORDER BY m."Sequence" LIMIT 32
                """).ToArrayAsync(ct);
            if (sequences.Length == 0) continue;
            var messages = await database.Messages.AsNoTracking().Where(m => m.ConversationId == owner.Id && sequences.Contains(m.Sequence)).ToArrayAsync(ct);
            var ledger = StructuredMemory.Read(owner.MemoryLedgerJson).Entries.ToLookup(f => f.Sequence);
            foreach (var m in messages)
            {
                var facts = TemporalMemory.IndexFacts(m, ledger[m.Sequence]);
                var json = JsonSerializer.Serialize(facts, ProjectToolCatalog.Json);
                var topics = facts.Select(f => f.Key).Distinct().ToArray();
                var searchable = string.Join(" ", facts.Select(f => f.Key + " " + f.Quote));
                await database.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO chat.temporal_memory(owner,sequence,observed,topics,facts,search,version)
                    SELECT {owner.Id},{m.Sequence},{m.CreatedAtUtc},{topics},{json}::jsonb,to_tsvector('french',{searchable}),{Version}
                    FROM chat.conversations WHERE "Id" = {owner.Id}
                    ON CONFLICT(owner,sequence) DO UPDATE SET topics=excluded.topics,facts=excluded.facts,search=excluded.search,version=excluded.version
                    WHERE chat.temporal_memory.version <> excluded.version
                    """, ct);
            }
        }
    }

    public async Task<TemporalRecall> RecallAsync(Guid owner, string question, ChatOptions settings, int through, CancellationToken ct)
    {
        var before = TemporalMemory.AsOf(question) ?? DateTimeOffset.MaxValue;
        var indexed = await database.Database.SqlQuery<int>($"""
            SELECT count(*)::int AS "Value" FROM chat.temporal_memory WHERE owner={owner} AND sequence<={through} AND version={Version}
            """).SingleAsync(ct);
        var query = string.Join(" | ", ProjectRetrieval.Tokens(question).Distinct().Take(24));
        // The lexical shortlist and the most recent observations are both scoped before scoring.
        var candidates = await database.Database.SqlQuery<string>($"""
            SELECT facts::text AS "Value" FROM chat.temporal_memory
            WHERE owner={owner} AND observed<{before} AND sequence<={through} AND version={Version}
            ORDER BY ts_rank(search,websearch_to_tsquery('french',{query.Replace(" | ", " OR ")})) DESC, sequence DESC LIMIT 96
            """).ToListAsync(ct);
        MemoryVectorMatches matches = new(new Dictionary<int, double>(), []);
        if (indexed > 0 && settings.RetrievalMode == "hybrid" && settings.EmbedHistory && settings.EmbeddingModel.Length > 0)
        {
            matches = await vectors.SearchMemoryAsync(owner, question, settings, through, before, ct);
            var ids = matches.Scores.Keys.ToArray();
            if (ids.Length > 0)
                candidates.AddRange(await database.Database.SqlQuery<string>($"""
                    SELECT facts::text AS "Value" FROM chat.temporal_memory
                    WHERE owner={owner} AND observed<{before} AND sequence<={through} AND sequence=ANY({ids}) AND version={Version}
                    """).ToListAsync(ct));
        }
        var groupScores = settings.Enrichment.Enabled && settings.EmbedHistory && knowledge is not null
            ? await knowledge.HistoryAsync(owner, question, settings.Enrichment.Profile, through, before, ct)
            : new Dictionary<int, double>();
        if (groupScores.Count > 0)
        {
            var sequences = groupScores.Keys.ToArray();
            candidates.AddRange(await database.Database.SqlQuery<string>($"""
                SELECT facts::text AS "Value" FROM chat.temporal_memory WHERE owner={owner} AND observed<{before}
                AND sequence=ANY({sequences}) AND sequence<={through} AND version={Version}
                """).ToArrayAsync(ct));
        }
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
        var variants = await database.Database.SqlQuery<string>($"""
            SELECT facts::text AS "Value" FROM chat.temporal_memory
            WHERE owner={owner} AND observed<{before} AND sequence<={through} AND version={Version} AND topics && {terms}
            ORDER BY sequence LIMIT 257
            """).ToListAsync(ct);
        if (variants.Count > 256) return new([], indexed, through, calls, "Trop de variantes pour restituer leurs relations sans omission.");
        return new(Decode(variants).Where(f => terms.Contains(f.Key)).OrderBy(f => Array.IndexOf(terms, f.Key)).ThenBy(f => f.Sequence).ToArray(),
            indexed, through, calls, warning);
    }

    public async Task<TemporalFact[]> ExportAsync(Guid owner, int after, int limit, CancellationToken ct)
    {
        var rows = await database.Database.SqlQuery<string>($"""
            SELECT facts::text AS "Value" FROM chat.temporal_memory WHERE owner={owner} AND sequence>{after} ORDER BY sequence LIMIT {limit}
            """).ToArrayAsync(ct);
        return Decode(rows);
    }
    public Task<int> ClearAsync(Guid owner, CancellationToken ct) =>
        database.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM chat.temporal_memory WHERE owner={owner}", ct);

    private static TemporalFact[] Decode(IEnumerable<string> rows) => rows.SelectMany(row =>
        JsonSerializer.Deserialize<TemporalFact[]>(row, ProjectToolCatalog.Json) ?? []).ToArray();
}
