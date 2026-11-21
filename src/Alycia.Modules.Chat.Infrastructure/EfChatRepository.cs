using Alycia.Contracts.Retrieval;
using Alycia.Contracts.Context;
using Alycia.Contracts.Workspaces;
using Alycia.Modules.Chat.Application;
using Alycia.Modules.Chat.Domain;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Alycia.Modules.Chat.Infrastructure;

public sealed class EfChatRepository(ChatDbContext database, IWorkspaceProfiles profiles, ITurnDocuments documents) : IChatRepository
{
    public async Task<IChatTurnScope?> BeginTurnAsync(Guid id, CancellationToken ct)
    {
        if (!await database.Conversations.AnyAsync(c => c.Id == id && c.WorkspaceId != null, ct)) return null;
        var transaction = await database.Database.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead, ct);
        return new TurnScope(transaction, database, id);
    }

    private sealed class TurnScope(Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction,
        ChatDbContext context, Guid conversationId) : IChatTurnScope
    {
        private bool committed;

        public async Task CommitAsync(CancellationToken ct)
        {
            await transaction.CommitAsync(ct);
            committed = true;
        }

        public async ValueTask DisposeAsync()
        {
            try { await transaction.DisposeAsync(); }
            finally
            {
                if (!committed) DetachTurn(context, conversationId);
            }
        }
    }

    // A subsequent lookup in the same scope must re-read the authoritative outcome from PostgreSQL.
    private static void DetachTurn(ChatDbContext context, Guid id, Guid? requestId = null)
    {
        foreach (var entry in context.ChangeTracker.Entries<ChatMessage>().Where(e => e.Entity.ConversationId == id &&
            (!requestId.HasValue || e.Entity.RequestId == requestId)).ToArray())
            entry.State = EntityState.Detached;
        foreach (var entry in context.ChangeTracker.Entries<Conversation>().Where(e => e.Entity.Id == id).ToArray())
            entry.State = EntityState.Detached;
    }

    public Task<IReadOnlyList<TurnFile>> ContextFilesAsync(Guid id, int first, CancellationToken ct) =>
        documents.ContextFilesAsync(id, first, ct);

    public async Task<int?> MessageCountAsync(Guid id, CancellationToken ct) =>
        (await FindAsync(id, ct))?.MessageCount;

    private static readonly System.Linq.Expressions.Expression<Func<ChatMessage, HistoryMessage>> HistoryProjection =
        m => new HistoryMessage(m.ConversationId, m.Sequence, m.RequestId, m.Role, m.Content, m.CreatedAtUtc);

    private IQueryable<ChatMessage> RecentPage(Guid id, int limit, int? before) =>
        database.Messages.AsNoTracking().Where(m => m.ConversationId == id && (before == null || m.Sequence < before))
            .OrderByDescending(m => m.Sequence).Take(limit);

    public async Task<IReadOnlyList<QueryHistoryItem>> RecentForQueryAsync(Guid id, int limit, CancellationToken ct)
    {
        var messages = await RecentPage(id, limit, null)
            .Select(m => new QueryHistoryItem(m.ConversationId, m.Sequence, m.Role, m.Content)).ToListAsync(ct);
        messages.Reverse();
        return messages;
    }

    public async Task<IReadOnlyList<HistoryMessage>> RecentAsync(Guid id, int limit, int? before, CancellationToken ct)
    {
        var messages = await RecentPage(id, limit, before).Select(HistoryProjection).ToListAsync(ct);
        messages.Reverse();
        return messages;
    }

    public async Task<IReadOnlyList<HistoryMessage>> SummaryRangeAsync(Guid id, int after, int through, CancellationToken ct) =>
        await database.Messages.AsNoTracking().Where(m => m.ConversationId == id && m.Sequence > after && m.Sequence <= through)
            .OrderBy(m => m.Sequence).Select(HistoryProjection).ToListAsync(ct);

    public async Task<IReadOnlyList<Conversation>> ListAsync(CancellationToken cancellationToken) =>
        await database.Conversations.AsNoTracking().OrderByDescending(item => item.UpdatedAtUtc)
            .ThenBy(item => item.Id).ToListAsync(cancellationToken);

    public async Task<Conversation?> FindAsync(Guid id, CancellationToken cancellationToken)
    {
        var conversation = await database.Conversations.SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (conversation?.WorkspaceId is Guid project)
        {
            var profile = await profiles.FindAsync(project, cancellationToken) ?? throw new ChatConflictException();
            conversation.ProjectSnapshot = new(profile.Revision, profile.Instructions, profile.Notes.Select(n => n.Text).ToArray());
        }
        return conversation;
    }

    public async Task AddAsync(Conversation conversation, CancellationToken cancellationToken)
    {
        database.Conversations.Add(conversation);
        await SaveAsync(cancellationToken);
    }

    public Task SaveAsync(CancellationToken cancellationToken) => database.SaveVersionedAsync(cancellationToken);

    public async Task DeleteAsync(Conversation conversation, CancellationToken cancellationToken)
    {
        database.Conversations.Remove(conversation);
        await SaveAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ChatMessage>> MessagesAsync(Guid id, int limit, int? before,
        CancellationToken cancellationToken)
    {
        var messages = await RecentPage(id, limit, before).ToListAsync(cancellationToken);
        messages.Reverse();
        return messages;
    }

    public async Task<IReadOnlyList<ChatMessage>> FindTurnAsync(Guid id, Guid requestId,
        CancellationToken cancellationToken) =>
        await database.Messages.AsNoTracking()
            .Where(item => item.ConversationId == id && item.RequestId == requestId)
            .OrderBy(item => item.Sequence).ToListAsync(cancellationToken);

    public async Task AppendTurnAsync(Conversation conversation, ChatMessage user, ChatMessage assistant,
        CancellationToken cancellationToken)
    {
        try
        {
            database.Messages.AddRange(user, assistant);
            await SaveAsync(cancellationToken);
        }
        catch
        {
            DetachTurn(database, conversation.Id, user.RequestId);
            throw;
        }
    }
}
