using System.Data;
using System.Data.Common;
using Alycia.Modules.Chat.Application;
using Alycia.Modules.Chat.Domain;
using Microsoft.EntityFrameworkCore;

namespace Alycia.Modules.Chat.Infrastructure;

public sealed record ConversationSearchPage(IReadOnlyList<Conversation> Items, bool HasMore);

public sealed class ConversationHistory(ChatDbContext database, ChatOptions defaults, FileStorage storage)
{
    public async Task<ConversationSearchPage> SearchAsync(string? q, int? offset, Guid? workspaceId, bool? unassigned, CancellationToken ct)
    {
        var query = q?.Trim() ?? "";
        if (query.Length > 200 || offset is < 0 or > 100000) throw new ArgumentException("Recherche invalide.");
        var pattern = "%" + query.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
        var matches = database.Conversations.AsNoTracking().Where(c => query == "" || EF.Functions.ILike(c.Title, pattern, "\\")
            || database.Messages.Any(m => m.ConversationId == c.Id && EF.Functions.ILike(m.Content, pattern, "\\")));
        if (workspaceId.HasValue || unassigned == true) matches = matches.Where(c => c.WorkspaceId == workspaceId);
        var items = await matches.OrderByDescending(c => c.UpdatedAtUtc).ThenBy(c => c.Id)
            .Skip(offset ?? 0).Take(51).ToListAsync(ct);
        return new(items.Take(50).ToArray(), items.Count > 50);
    }

    public Task<ConversationArchive> ArchiveAsync(Guid id, CancellationToken ct) => ArchiveAsync(id, null, ct);

    public async Task<Conversation> ImportAsync(byte[] bytes, CancellationToken ct)
    {
        var restored = ConversationTransfer.Restore(ConversationTransfer.Parse(bytes));
        database.Conversations.Add(restored.Conversation);
        database.Messages.AddRange(restored.Messages);
        await database.SaveChangesAsync(ct);
        return restored.Conversation;
    }

    public async Task<Conversation> ForkAsync(Guid id, int revision, int? throughSequence, CancellationToken ct)
    {
        var original = await database.Conversations.AsNoTracking().SingleOrDefaultAsync(c => c.Id == id, ct)
            ?? throw new ConversationNotFoundException();
        ConversationConfiguration.CheckRevision(original, revision);
        var through = throughSequence ?? original.MessageCount;
        if (through < 0 || through > original.MessageCount || through % 2 != 0)
            throw new ArgumentException("Une copie doit s’arrêter après un échange complet.");
        var archive = await ArchiveAsync(id, through, ct);
        archive = archive with { Title = ("Copie · " + archive.Title)[..Math.Min(100, archive.Title.Length + 8)] };
        var restored = ConversationTransfer.Restore(archive);
        restored.Conversation.AssignWorkspace(original.WorkspaceId);
        var files = await database.Attachments.AsNoTracking().Where(f => f.ConversationId == id
            && f.AnchorSequence <= through + 1).ToListAsync(ct);
        var copyId = restored.Conversation.Id;
        var copyDirectory = storage.DirectoryFor(copyId);
        if (files.Count > 0)
        {
            if (Directory.Exists(copyDirectory)) throw new IOException("Le répertoire de la copie existe déjà.");
            Directory.CreateDirectory(copyDirectory);
        }
        var written = new List<string>();
        var forkEntities = new List<object>(restored.Messages) { restored.Conversation };
        var commitStarted = false;
        try
        {
            var copies = await CopyAttachmentsAsync(files, id, copyId, written, ct);
            // Detect changes while the file copies were being prepared.
            if (!await database.Conversations.AnyAsync(c => c.Id == id && c.Revision == revision, ct))
                throw new ChatConflictException();
            if (original.WorkspaceId.HasValue)
            {
                var localModes = await database.WorkspaceFileOverrides.AsNoTracking().Where(o => o.ConversationId == id).ToArrayAsync(ct);
                forkEntities.AddRange(localModes.Select(o => new WorkspaceFileOverride
                { ConversationId = copyId, FileId = o.FileId, Mode = o.Mode }));
            }
            forkEntities.AddRange(copies);
            await using var transaction = await database.Database.BeginTransactionAsync(ct);
            database.AddRange(forkEntities);
            await database.SaveChangesAsync(acceptAllChangesOnSuccess: false, cancellationToken: ct);
            ct.ThrowIfCancellationRequested();
            commitStarted = true;
            await transaction.CommitAsync(ct);
            database.ChangeTracker.AcceptAllChanges();
            return restored.Conversation;
        }
        catch
        {
            // A retry in this scope must not insert the abandoned fork.
            foreach (var entity in forkEntities) database.Entry(entity).State = EntityState.Detached;
            // Before COMMIT, disposing the transaction rolls back even if SaveChanges ran.
            if (files.Count > 0 && (!commitStarted || await CanDiscardFailedCommitAsync(copyId)))
            {
                foreach (var path in written) File.Delete(path);
                if (!Directory.EnumerateFileSystemEntries(copyDirectory).Any()) Directory.Delete(copyDirectory);
            }
            throw;
        }
    }

    private async Task<bool> CanDiscardFailedCommitAsync(Guid copyId)
    {
        // The fork's INSERT holds ROW EXCLUSIVE until PostgreSQL resolves COMMIT.
        // A negative read without this barrier could miss a still-running fork.
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        try
        {
            await using var probe = await database.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, timeout.Token);
            await database.Database.ExecuteSqlRawAsync("LOCK TABLE chat.conversations IN SHARE MODE NOWAIT", timeout.Token);
            return !await database.Conversations.AsNoTracking().AnyAsync(c => c.Id == copyId, timeout.Token);
        }
        catch (Exception ex) when (ex is DbException or IOException or TimeoutException or OperationCanceledException or InvalidOperationException)
        {
            // An unavailable connection, lock or verdict cannot justify deleting originals.
            return false;
        }
    }

    private async Task<List<Attachment>> CopyAttachmentsAsync(IReadOnlyList<Attachment> files, Guid sourceId, Guid copyId,
        List<string> written, CancellationToken ct)
    {
        var ids = files.ToDictionary(f => f.Id, _ => Guid.NewGuid());
        var copies = new List<Attachment>();
        foreach (var file in files)
        {
            var newId = ids[file.Id];
            await using var source = File.OpenRead(storage.PathFor(sourceId, file.Id));
            var path = storage.PathFor(copyId, newId);
            await using var target = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            written.Add(path);
            await source.CopyToAsync(target, ct);
            copies.Add(new Attachment
            {
                Id = newId,
                ConversationId = copyId,
                BatchId = copyId,
                ArchiveId = file.ArchiveId.HasValue ? ids.GetValueOrDefault(file.ArchiveId.Value) : null,
                Name = file.Name,
                Text = file.Text,
                Status = file.Status,
                Size = file.Size,
                Sha256 = file.Sha256,
                CreatedAtUtc = file.CreatedAtUtc,
                AnchorSequence = file.AnchorSequence,
                SourceCreatedAt = file.SourceCreatedAt,
                SourceModifiedAt = file.SourceModifiedAt,
                MetadataSource = file.MetadataSource,
                ContextMode = file.ContextMode
            });
        }
        return copies;
    }

    private async Task<ConversationArchive> ArchiveAsync(Guid id, int? through, CancellationToken ct)
    {
        var conversation = await database.Conversations.AsNoTracking().SingleOrDefaultAsync(c => c.Id == id, ct)
            ?? throw new ConversationNotFoundException();
        var limit = through ?? conversation.MessageCount;
        if (limit > ConversationTransfer.MaxMessages) throw new ArgumentException("Export limité à 2000 messages : utilisez la sauvegarde complète.");
        var query = database.Messages.AsNoTracking().Where(m => m.ConversationId == id && m.Sequence <= limit);
        if (await query.SumAsync(m => (long)m.Content.Length + m.ToolTraceJson.Length + m.DiagnosticsJson.Length, ct) > ConversationTransfer.MaxBytes)
            throw new ArgumentException("Export trop volumineux : utilisez la sauvegarde complète.");
        var messages = await query.OrderBy(m => m.Sequence)
            .Select(m => new ArchivedMessage(m.Role, m.Content, m.CreatedAtUtc, m.ToolTraceJson, m.DiagnosticsJson)).ToArrayAsync(ct);
        if (messages.Length != limit) throw new ChatConflictException();
        return new(1, conversation.Title, conversation.Model, conversation.SystemPrompt, conversation.ContextNotes,
            ConversationConfiguration.Options(conversation, defaults), messages, conversation.WorkspaceId,
            conversation.WorkspaceId is Guid project ? await database.Attachments.AsNoTracking().Where(f => f.WorkspaceId == project)
                .OrderBy(f => f.Name).Select(f => f.Name + " · " + f.Sha256).ToArrayAsync(ct) : null);
    }
}
