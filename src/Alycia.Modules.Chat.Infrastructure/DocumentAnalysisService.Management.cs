using Alycia.Modules.Chat.Application;
using Alycia.Modules.Chat.Domain;
using Microsoft.EntityFrameworkCore;

namespace Alycia.Modules.Chat.Infrastructure;

public sealed partial class DocumentAnalysisService
{
    // The response writer holds this lease until the final progress or error message is delivered.
    public IDisposable AcquireAnalysisLease(Guid id) => GenerationGate.Shared.Enter(id);

    public async Task<string> InventoryAsync(Guid id, Guid? batch, CancellationToken ct)
    {
        await RequireConversationAsync(id, ct);
        return ProjectInventory.Tree(InBatch(await MetadataAsync(database, id, ct), batch));
    }

    public Task<DocumentAnalysis?> GetAsync(Guid id, Guid analysisId, CancellationToken ct) =>
        database.DocumentAnalyses.AsNoTracking().SingleOrDefaultAsync(a => a.Id == analysisId && a.ConversationId == id, ct);

    public async Task DeleteRunAsync(Guid id, Guid runId, CancellationToken ct)
    {
        using var lease = AcquireAnalysisLease(id);
        if (await new EfChatRepository(database).FindAsync(id, ct) is null) throw new ConversationNotFoundException();
        new AnalysisCheckpoints(reader.Storage).DeleteRun(id, runId);
    }

    public async Task<Conversation?> SelectAsync(Guid id, Guid analysisId, int revision, bool useInContext, CancellationToken ct)
    {
        using var lease = AcquireAnalysisLease(id);
        var conversation = await RequireRevisionAsync(id, revision, ct);
        var analysis = await database.DocumentAnalyses.SingleOrDefaultAsync(a => a.Id == analysisId && a.ConversationId == id, ct);
        if (analysis is null) return null;
        if (useInContext)
        {
            var files = await MetadataAsync(database, id, ct);
            if (ProjectInventory.Fingerprint(InBatch(files, analysis.ScopeBatchId)) != analysis.SourceFingerprint)
                throw new ArgumentException("Cette analyse est périmée : les sources ou exclusions ont changé. Relancez l’analyse.");
            foreach (var previous in await database.DocumentAnalyses.Where(a => a.ConversationId == id && a.UseInContext).ToListAsync(ct))
                previous.UseInContext = false;
        }
        analysis.UseInContext = useInContext;
        conversation.AttachFiles();
        await new EfChatRepository(database).SaveAsync(ct);
        return conversation;
    }

    public async Task<bool> DeleteAsync(Guid id, Guid analysisId, int revision, CancellationToken ct)
    {
        using var lease = AcquireAnalysisLease(id);
        var conversation = await RequireRevisionAsync(id, revision, ct);
        var analysis = await database.DocumentAnalyses.SingleOrDefaultAsync(a => a.Id == analysisId && a.ConversationId == id, ct);
        if (analysis is null) return false;
        database.DocumentAnalyses.Remove(analysis);
        conversation.AttachFiles();
        await new EfChatRepository(database).SaveAsync(ct);
        return true;
    }

    private async Task RequireConversationAsync(Guid id, CancellationToken ct)
    {
        if (!await database.Conversations.AnyAsync(c => c.Id == id, ct)) throw new ConversationNotFoundException();
    }

    private async Task<Conversation> RequireRevisionAsync(Guid id, int revision, CancellationToken ct)
    {
        var conversation = await database.Conversations.SingleOrDefaultAsync(c => c.Id == id, ct)
            ?? throw new ConversationNotFoundException();
        ConversationConfiguration.CheckRevision(conversation, revision);
        return conversation;
    }

    private static IEnumerable<InventoryFile> InBatch(IEnumerable<InventoryFile> files, Guid? batch) =>
        files.Where(f => !batch.HasValue || f.BatchId == batch);

    public async Task<AnalysisRun[]> RunsAsync(Guid id, CancellationToken ct)
    {
        await RequireConversationAsync(id, ct);
        return await new AnalysisCheckpoints(reader.Storage).ListAsync(id, ct);
    }

    public async Task<IReadOnlyList<AnalysisItem>> ListAsync(Guid id, CancellationToken ct)
    {
        await RequireConversationAsync(id, ct);
        var metadata = await MetadataAsync(database, id, ct);
        var reports = await database.DocumentAnalyses.AsNoTracking().Where(a => a.ConversationId == id)
            .OrderByDescending(a => a.CreatedAtUtc).Select(a => new
            {
                a.Id,
                a.Title,
                a.CreatedAtUtc,
                a.Model,
                a.FileCount,
                a.SummaryCalls,
                a.UseInContext,
                a.ScopeBatchId,
                a.SourceFingerprint
            }).ToArrayAsync(ct);
        return reports.Select(a => new AnalysisItem(a.Id, a.Title, a.CreatedAtUtc, a.Model, a.FileCount, a.SummaryCalls,
            a.UseInContext, ProjectInventory.Fingerprint(InBatch(metadata, a.ScopeBatchId))
                != a.SourceFingerprint, a.ScopeBatchId)).ToArray();
    }

    public static async Task<InventoryFile[]> MetadataAsync(ChatDbContext database, Guid id, CancellationToken ct) =>
        await database.ModelSources(id)
            .Select(f => new InventoryFile(f.Id, f.BatchId, f.ArchiveId, f.Name, f.Size, f.Sha256,
                Path.GetExtension(f.Name), f.SourceCreatedAt, f.SourceModifiedAt, f.MetadataSource,
                f.CreatedAtUtc, f.Status, f.ContextMode)).ToArrayAsync(ct);
}
