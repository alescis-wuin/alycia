using System.Data;
using System.Data.Common;
using Alycia.Platform.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Alycia.Modules.Documents.Infrastructure;

public sealed class DocumentScope(Func<RelationalSession, IConversationWorkspaceLookup> membership, Func<DbConnection, DbContext> enlist)
{
    internal async Task<T> ReadAsync<T>(DbContext db, Guid id, bool project, bool model,
        Func<IQueryable<Attachment>, Task<T>> read, CancellationToken ct)
    {
        if (project) return await read(db.Set<Attachment>().AsNoTracking().Where(f => f.WorkspaceId == id));
        await using var snapshot = db.Database.CurrentTransaction is null
            ? await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct) : null;
        var workspace = await membership(new RelationalSession(db.Database)).FindWorkspaceAsync(id, ct);
        var files = ForConversation(db, id, workspace);
        if (model)
            files = files.Where(file => file.WorkspaceId != null || !db.Set<Attachment>().Any(other =>
                workspace.HasValue && other.WorkspaceId == workspace && other.Name == file.Name && other.Sha256 == file.Sha256));
        var result = await read(files);
        if (snapshot is not null) await snapshot.CommitAsync(ct);
        return result;
    }

    // A missing override or a newly imported duplicate can change eligibility. A row lock
    // on the original alone cannot protect those predicates against an INSERT.
    public async Task<HashSet<(Guid Id, string Hash)>> EligibleHashesAsync(DbTransaction transaction, Guid owner, bool project,
        IReadOnlySet<(Guid Id, string Hash)> hashes, bool publishing, CancellationToken ct)
    {
        var connection = transaction.Connection ?? throw new InvalidOperationException("Transaction terminée.");
        await using var db = enlist(connection);
        await db.Database.UseTransactionAsync(transaction, ct);
        if (publishing)
            await db.Database.ExecuteSqlRawAsync("LOCK TABLE chat.attachments, chat.workspace_file_overrides IN SHARE MODE NOWAIT", ct);
        var ids = hashes.Select(f => f.Id).Distinct().ToArray();
        var visible = await ReadAsync(db, owner, project, true, q => q.Where(f =>
            ids.Contains(f.Id) && f.ContextMode != "excluded" && f.Text != "")
            .Select(f => new { f.Id, f.Sha256 }).ToArrayAsync(ct), ct);
        return visible.Select(f => (f.Id, f.Sha256)).Where(hashes.Contains).ToHashSet();
    }

    // Every retrieval, analysis and tool read starts from the same membership predicate.
    // A detached projection carries the effective mode without modifying the shared original.
    private static IQueryable<Attachment> ForConversation(DbContext db, Guid id, Guid? workspace) =>
        from file in db.Set<Attachment>().AsNoTracking()
        where file.ConversationId == id || (workspace.HasValue && file.WorkspaceId == workspace)
        let local = db.Set<WorkspaceFileOverride>().Where(o => o.ConversationId == id && o.FileId == file.Id)
            .Select(o => o.Mode).FirstOrDefault()
        select new Attachment
        {
            Id = file.Id,
            ConversationId = file.ConversationId,
            WorkspaceId = file.WorkspaceId,
            Name = file.Name,
            Size = file.Size,
            Sha256 = file.Sha256,
            Status = file.Status,
            Text = file.Text,
            ContextMode = file.ContextMode == "excluded" || (file.WorkspaceId != null && db.Set<Attachment>().Any(other =>
                other.ConversationId == id && other.Name == file.Name && other.Sha256 == file.Sha256 && other.ContextMode == "excluded"))
                ? "excluded" : local ?? file.ContextMode,
            BatchId = file.BatchId,
            ArchiveId = file.ArchiveId,
            AnchorSequence = file.AnchorSequence,
            CreatedAtUtc = file.CreatedAtUtc,
            SourceCreatedAt = file.SourceCreatedAt,
            SourceModifiedAt = file.SourceModifiedAt,
            MetadataSource = file.MetadataSource
        };

}
