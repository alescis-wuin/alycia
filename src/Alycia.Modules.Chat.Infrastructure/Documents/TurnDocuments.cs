using Alycia.Modules.Chat.Application;
using Alycia.Modules.Chat.Domain;
using Microsoft.EntityFrameworkCore;

namespace Alycia.Modules.Chat.Infrastructure;

public sealed class TurnDocuments(ChatDbContext database, DocumentScope scope) : ITurnDocuments
{
    private static TurnFile Project(Attachment f) => new(
        f.Id, f.WorkspaceId, f.ConversationId, f.BatchId, f.ArchiveId, f.Name, f.Size, f.Sha256,
        f.Status, f.Text, f.AnchorSequence, f.CreatedAtUtc, f.SourceCreatedAt, f.SourceModifiedAt, f.MetadataSource, f.ContextMode);

    public async Task<IReadOnlyList<TurnFile>> FilesAsync(Guid id, CancellationToken cancellationToken) =>
        (await scope.ReadAsync(database, id, false, false, q => q
            .OrderBy(f => f.CreatedAtUtc).ToListAsync(cancellationToken), cancellationToken)).Select(Project).ToArray();

    public async Task<IReadOnlyList<TurnFile>> ContextFilesAsync(Guid id, int first, CancellationToken ct)
    {
        var files = await scope.ReadAsync(database, id, false, true, q => q.Where(f => f.ContextMode != "excluded"
            && ((f.ContextMode == "pinned" || f.ContextMode == "project") || f.AnchorSequence >= first) && f.Text != "")
            .OrderBy(f => f.CreatedAtUtc).ToListAsync(ct), ct);
        files = files.OrderByDescending(f => f.ContextMode == "pinned").DistinctBy(f => (f.Name, f.Sha256)).ToList();
        var summaries = await database.DocumentAnalyses.AsNoTracking().Where(a => a.ConversationId == id && a.UseInContext).ToArrayAsync(ct);
        if (summaries.Length == 0) return files.Select(Project).ToArray();
        var metadata = await DocumentAnalysisService.MetadataAsync(database, scope, id, ct);
        foreach (var summary in summaries)
        {
            var selected = metadata.Where(f => !summary.ScopeBatchId.HasValue || f.BatchId == summary.ScopeBatchId);
            if (ProjectInventory.Fingerprint(selected) != summary.SourceFingerprint) continue;
            // Summaries orient the response; original evidence remains eligible for retrieval.
            files.Add(new Attachment
            {
                Id = summary.Id,
                Name = "Synthèse du projet · " + summary.Title,
                Text = "Résumé dérivé ; inventaire et provenance disponibles dans Analyses.\n" + summary.SummaryText,
                Sha256 = MemoryTransfer.Hash(summary.SummaryText),
                Status = "derived_summary",
                ContextMode = "pinned",
                AnchorSequence = 1,
                CreatedAtUtc = summary.CreatedAtUtc
            });
        }
        return files.Select(Project).ToArray();
    }

}
