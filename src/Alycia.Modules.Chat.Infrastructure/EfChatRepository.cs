using Alycia.Contracts.Retrieval;
using Alycia.Contracts.Context;
using Alycia.Contracts.Workspaces;
using Alycia.Modules.Chat.Application;
using Alycia.Modules.Chat.Domain;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Alycia.Modules.Chat.Infrastructure;

public sealed class EfChatRepository(DbContext database, IWorkspaceProfiles profiles) : IChatRepository
{
    public async Task<IChatTurnScope?> BeginTurnAsync(Guid id, CancellationToken ct)
    {
        if (!await database.Set<Conversation>().AnyAsync(c => c.Id == id && c.WorkspaceId != null, ct)) return null;
        var transaction = await database.Database.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead, ct);
        return new TurnScope(transaction, database, id);
    }

    private sealed class TurnScope(Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction,
        DbContext context, Guid conversationId) : IChatTurnScope
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
    private static void DetachTurn(DbContext context, Guid id, Guid? requestId = null)
    {
        foreach (var entry in context.ChangeTracker.Entries<ChatMessage>().Where(e => e.Entity.ConversationId == id &&
            (!requestId.HasValue || e.Entity.RequestId == requestId)).ToArray())
            entry.State = EntityState.Detached;
        foreach (var entry in context.ChangeTracker.Entries<Conversation>().Where(e => e.Entity.Id == id).ToArray())
            entry.State = EntityState.Detached;
    }

    public async Task<IReadOnlyList<Conversation>> ListAsync(CancellationToken cancellationToken) =>
        await database.Set<Conversation>().AsNoTracking().OrderByDescending(item => item.UpdatedAtUtc)
            .ThenBy(item => item.Id).ToListAsync(cancellationToken);

    public async Task<Conversation?> FindAsync(Guid id, CancellationToken cancellationToken)
    {
        var conversation = await database.Set<Conversation>().SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (conversation?.WorkspaceId is Guid project)
        {
            var profile = await profiles.FindAsync(project, cancellationToken) ?? throw new ChatConflictException();
            conversation.ProjectSnapshot = new(profile.Revision, profile.Instructions, profile.Notes.Select(n => n.Text).ToArray());
        }
        return conversation;
    }

    public async Task AddAsync(Conversation conversation, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var entry = database.Set<Conversation>().Add(conversation);
        try { await SaveAsync(cancellationToken); }
        catch { entry.State = EntityState.Detached; throw; }
    }

    public Task SaveAsync(CancellationToken cancellationToken) => database.SaveVersionedAsync(cancellationToken);

    public async Task DeleteAsync(Conversation conversation, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var entry = database.Set<Conversation>().Remove(conversation);
        try { await SaveAsync(cancellationToken); }
        catch { entry.State = EntityState.Detached; throw; }
    }

    public async Task<IReadOnlyList<ChatMessage>> MessagesAsync(Guid id, int limit, int? before,
        CancellationToken cancellationToken)
    {
        var messages = await ConversationReadModel.RecentPage(database, id, limit, before).ToListAsync(cancellationToken);
        messages.Reverse();
        return messages;
    }

    public async Task<IReadOnlyList<ChatMessage>> FindTurnAsync(Guid id, Guid requestId,
        CancellationToken cancellationToken) =>
        await database.Set<ChatMessage>().AsNoTracking()
            .Where(item => item.ConversationId == id && item.RequestId == requestId)
            .OrderBy(item => item.Sequence).ToListAsync(cancellationToken);

    public async Task AppendTurnAsync(Conversation conversation, ChatMessage user, ChatMessage assistant,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (user.ConversationId != conversation.Id || assistant.ConversationId != conversation.Id ||
            user.RequestId != assistant.RequestId || user.Role != "user" || assistant.Role != "assistant" ||
            assistant.Sequence != user.Sequence + 1)
            throw new ArgumentException("Les deux messages doivent former un tour de la même conversation.");
        try
        {
            database.Set<ChatMessage>().AddRange(user, assistant);
            await SaveAsync(cancellationToken);
        }
        catch
        {
            DetachTurn(database, conversation.Id, user.RequestId);
            throw;
        }
    }
}
