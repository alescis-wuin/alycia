using System.Collections.Immutable;
using Alycia.Contracts.Retrieval;
using Alycia.Contracts.Context;
using Alycia.Contracts.Workspaces;
using Alycia.Modules.Chat.Application;
using Alycia.Modules.Chat.Domain;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Alycia.Modules.Chat.Infrastructure;

public sealed class ConversationReadModel(DbContext database, ITurnDocuments documents)
    : IConversationContextReader, IToolHistoryReader, IQuerySources
{
    public Task<IReadOnlyList<TurnFile>> ContextFilesAsync(Guid id, int first, CancellationToken ct) =>
        documents.ContextFilesAsync(id, first, ct);

    public async Task<int?> MessageCountAsync(Guid id, CancellationToken ct) =>
        await database.Set<Conversation>().AsNoTracking().Where(c => c.Id == id).Select(c => (int?)c.MessageCount).SingleOrDefaultAsync(ct);

    private static readonly System.Linq.Expressions.Expression<Func<ChatMessage, HistoryMessage>> HistoryProjection =
        m => new HistoryMessage(m.ConversationId, m.Sequence, m.RequestId, m.Role, m.Content, m.CreatedAtUtc);

    internal static IQueryable<ChatMessage> RecentPage(DbContext database, Guid id, int limit, int? before) =>
        database.Set<ChatMessage>().AsNoTracking().Where(m => m.ConversationId == id && (before == null || m.Sequence < before))
            .OrderByDescending(m => m.Sequence).Take(limit);

    public async Task<IReadOnlyList<QueryHistoryItem>> RecentForQueryAsync(Guid id, int limit, CancellationToken ct)
    {
        var messages = await RecentPage(database, id, limit, null)
            .Select(m => new QueryHistoryItem(m.ConversationId, m.Sequence, m.Role, m.Content)).ToListAsync(ct);
        messages.Reverse();
        return messages.ToImmutableArray();
    }

    public async Task<IReadOnlyList<HistoryMessage>> RecentAsync(Guid id, int limit, int? before, CancellationToken ct)
    {
        var messages = await RecentPage(database, id, limit, before).Select(HistoryProjection).ToListAsync(ct);
        messages.Reverse();
        return messages.ToImmutableArray();
    }

    public async Task<IReadOnlyList<HistoryMessage>> SummaryRangeAsync(Guid id, int after, int through, CancellationToken ct) =>
        (await database.Set<ChatMessage>().AsNoTracking().Where(m => m.ConversationId == id && m.Sequence > after && m.Sequence <= through)
            .OrderBy(m => m.Sequence).Select(HistoryProjection).ToListAsync(ct)).ToImmutableArray();
}
