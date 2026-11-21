using Alycia.Contracts.Workspaces;
using Alycia.Modules.Chat.Application;
using Alycia.Modules.Chat.Domain;
using Microsoft.EntityFrameworkCore;

namespace Alycia.Modules.Chat.Infrastructure;

public sealed record SourcePreview(string Name, string Sha256, string Text, int Start, int End, bool Partial);

public sealed class ProjectFiles(ChatDbContext db, FileImport importer, ProjectSourceReader reader, IWorkspaceProfiles profiles)
{
    public async Task<IReadOnlyList<FileSummary>> ShareAsync(Guid conversation, Guid file, CancellationToken ct)
    {
        var chat = await db.Conversations.AsNoTracking().SingleOrDefaultAsync(c => c.Id == conversation, ct)
            ?? throw new ConversationNotFoundException();
        var project = chat.WorkspaceId ?? throw new ArgumentException("Rattachez la conversation à un projet.");
        var attachment = await db.Attachments.AsNoTracking().SingleOrDefaultAsync(f => f.ConversationId == conversation && f.Id == file, ct)
            ?? throw new ArgumentException("Pièce jointe introuvable.");
        if (attachment.ContextMode == "excluded") throw new ArgumentException("Une source exclue ne peut pas être promue.");
        await using var original = reader.OpenOriginal(attachment);
        return await importer.ImportAsync(project, attachment.Id, [new(attachment.Name, original, attachment.SourceCreatedAt,
            attachment.SourceModifiedAt, attachment.MetadataSource)], ct, workspace: true);
    }

    public async Task<FileSummary[]> ListAsync(Guid project, CancellationToken ct)
    {
        await profiles.RequireAsync(project, true, ct);
        return await db.Attachments.AsNoTracking().Where(f => f.WorkspaceId == project)
            .OrderBy(f => f.Name).ThenBy(f => f.Id).Select(f => new FileSummary(f.Id, f.Name, f.Size, f.Status, f.AnchorSequence,
                f.CreatedAtUtc, f.ArchiveId, f.Sha256, f.ContextMode, f.BatchId, f.SourceCreatedAt, f.SourceModifiedAt, f.MetadataSource, f.WorkspaceId))
            .ToArrayAsync(ct);
    }

    public async Task<FileSummary> SetModeAsync(Guid project, Guid file, string mode, int revision, CancellationToken ct)
    {
        var profile = await RequireRevisionAsync(project, revision, ct);
        var attachment = await db.Attachments.SingleOrDefaultAsync(f => f.WorkspaceId == project && f.Id == file, ct)
            ?? throw new ArgumentException("Document absent de ce projet.");
        if (mode is not ("project" or "pinned" or "excluded")) throw new ArgumentException("Mode de source invalide.");
        if (ImportPolicy.Excluded(attachment.Name) && mode != "excluded")
            throw new ArgumentException("Cette source est exclue par la politique d’import.");
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        attachment.ContextMode = mode;
        await profiles.AdvanceAsync(project, profile.Revision, ct);
        await db.SaveVersionedAsync(ct);
        await tx.CommitAsync(ct);
        return FileImport.Summary(attachment);
    }

    public async Task<bool> RemoveAsync(Guid project, Guid file, int revision, CancellationToken ct)
    {
        var profile = await RequireRevisionAsync(project, revision, ct);
        var owned = await db.Attachments.Where(f => f.WorkspaceId == project).Select(f => new { f.Id, f.ArchiveId }).ToArrayAsync(ct);
        if (!owned.Any(f => f.Id == file)) return false;
        var removed = new HashSet<Guid> { file };
        for (var count = -1; count != removed.Count;)
        {
            count = removed.Count;
            foreach (var attachment in owned)
                if (attachment.ArchiveId is Guid parent && removed.Contains(parent)) removed.Add(attachment.Id);
        }

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Attachments.Where(f => f.WorkspaceId == project && removed.Contains(f.Id)).ExecuteDeleteAsync(ct);
        await profiles.AdvanceAsync(project, profile.Revision, ct);
        await tx.CommitAsync(ct);
        // Retain immutable originals for already running snapshot reads and backups.
        return true;
    }

    public async Task ClearOverrideAsync(Guid conversation, Guid file, int revision, CancellationToken ct)
    {
        var chat = await db.Conversations.SingleOrDefaultAsync(c => c.Id == conversation, ct) ?? throw new ConversationNotFoundException();
        ConversationConfiguration.CheckRevision(chat, revision);
        var rule = await db.WorkspaceFileOverrides.FindAsync([conversation, file], ct);
        if (rule is null) return;
        db.Remove(rule);
        chat.AttachFiles();
        await db.SaveVersionedAsync(ct);
    }

    public async Task<SourcePreview?> PreviewAsync(Guid owner, bool workspace, Guid file, string? sha, int? start, int? end, CancellationToken ct)
    {
        var files = workspace ? db.Attachments.AsNoTracking().Where(f => f.WorkspaceId == owner) : db.ForConversation(owner);
        var attachment = await files.SingleOrDefaultAsync(f => f.Id == file, ct);
        if (attachment is null) return null;
        if (sha is not null && !string.Equals(sha, attachment.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Cette version de la source est indisponible.");
        var source = await reader.ReadAsync(attachment, ct);
        int from = start ?? 0;
        if (from < 0 || from > source.Text.Length || end.HasValue && end < from)
            throw new ArgumentException("Position source invalide.");
        int through = Math.Min(Math.Min(end ?? source.Text.Length, source.Text.Length), from + 12000);
        return new(attachment.Name, attachment.Sha256, source.Text[from..through], from, through,
            !source.Complete || from > 0 || through < source.Text.Length);
    }

    private async Task<ProjectProfile> RequireRevisionAsync(Guid project, int revision, CancellationToken ct)
    {
        var profile = await profiles.RequireAsync(project, false, ct);
        if (profile.Revision != revision) throw new ChatConflictException();
        return profile;
    }
}
