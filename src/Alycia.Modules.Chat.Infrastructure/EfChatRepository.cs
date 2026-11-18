using Alycia.Modules.Chat.Application;
using Alycia.Modules.Chat.Domain;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Alycia.Modules.Chat.Infrastructure;

public sealed class EfChatRepository(ChatDbContext database) : IChatRepository
{
    public async Task<IChatTurnScope?> BeginTurnAsync(Guid id, CancellationToken ct)
    {
        if (!await database.Conversations.AnyAsync(c => c.Id == id && c.WorkspaceId != null, ct)) return null;
        var transaction = await database.Database.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead, ct);
        return new TurnScope(transaction);
    }

    private sealed class TurnScope(Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction) : IChatTurnScope
    {
        public Task CommitAsync(CancellationToken ct) => transaction.CommitAsync(ct);
        public ValueTask DisposeAsync() => transaction.DisposeAsync();
    }

    public async Task<IReadOnlyList<Attachment>> FilesAsync(Guid id, CancellationToken cancellationToken) =>
        await database.ForConversation(id)
            .OrderBy(f => f.CreatedAtUtc).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Attachment>> ContextFilesAsync(Guid id, int first, CancellationToken ct)
    {
        var files = await database.ForConversation(id).Where(f => f.ContextMode != "excluded"
            && ((f.ContextMode == "pinned" || f.ContextMode == "project") || f.AnchorSequence >= first) && f.Text != "")
            .OrderBy(f => f.CreatedAtUtc).ToListAsync(ct);
        var summaries = await database.DocumentAnalyses.AsNoTracking().Where(a => a.ConversationId == id && a.UseInContext).ToArrayAsync(ct);
        if (summaries.Length == 0) return files;
        var metadata = await DocumentAnalysisService.MetadataAsync(database, id, ct);
        foreach (var summary in summaries)
        {
            var scope = metadata.Where(f => !summary.ScopeBatchId.HasValue || f.BatchId == summary.ScopeBatchId);
            if (ProjectInventory.Fingerprint(scope) != summary.SourceFingerprint) continue;
            // Summaries orient the response; original evidence remains eligible for retrieval.
            files.Add(new Attachment
            {
                Id = summary.Id,
                Name = "Synthèse du projet · " + summary.Title,
                Text = "Résumé dérivé ; inventaire et provenance disponibles dans Analyses.\n" + summary.SummaryText,
                Sha256 = StructuredMemory.Hash(summary.SummaryText),
                Status = "derived_summary",
                ContextMode = "pinned",
                AnchorSequence = 1,
                CreatedAtUtc = summary.CreatedAtUtc
            });
        }
        return files;
    }

    public async Task<IReadOnlyList<ChatMessage>> SummaryRangeAsync(Guid id, int after, int through, CancellationToken ct) =>
        await database.Messages.AsNoTracking().Where(m => m.ConversationId == id && m.Sequence > after && m.Sequence <= through)
            .OrderBy(m => m.Sequence).ToListAsync(ct);

    public async Task<IReadOnlyList<Conversation>> ListAsync(CancellationToken cancellationToken) =>
        await database.Conversations.AsNoTracking().OrderByDescending(item => item.UpdatedAtUtc)
            .ThenBy(item => item.Id).ToListAsync(cancellationToken);

    public async Task<Conversation?> FindAsync(Guid id, CancellationToken cancellationToken)
    {
        var conversation = await database.Conversations.SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (conversation?.WorkspaceId is Guid project)
            conversation.ProjectSnapshot = await database.WorkspaceProfiles.AsNoTracking().SingleAsync(p => p.Id == project, cancellationToken);
        return conversation;
    }

    public async Task AddAsync(Conversation conversation, CancellationToken cancellationToken)
    {
        database.Conversations.Add(conversation);
        await SaveAsync(cancellationToken);
    }

    public async Task SaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await database.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ChatConflictException();
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException
        { SqlState: PostgresErrorCodes.UniqueViolation or PostgresErrorCodes.ForeignKeyViolation or PostgresErrorCodes.SerializationFailure })
        {
            throw new ChatConflictException();
        }
    }

    public async Task DeleteAsync(Conversation conversation, CancellationToken cancellationToken)
    {
        database.Conversations.Remove(conversation);
        await SaveAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ChatMessage>> MessagesAsync(Guid id, int limit, int? before,
        CancellationToken cancellationToken)
    {
        var messages = await database.Messages.AsNoTracking()
            .Where(item => item.ConversationId == id && (before == null || item.Sequence < before))
            .OrderByDescending(item => item.Sequence).Take(limit).ToListAsync(cancellationToken);
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
        database.Messages.AddRange(user, assistant);
        await SaveAsync(cancellationToken);
    }
}
