using Microsoft.EntityFrameworkCore;

namespace Alycia.Modules.Documents.Infrastructure;

public sealed partial class DocumentAnalysisService
{
    // The response writer holds this lease until the final progress or error message is delivered.
    public IDisposable AcquireAnalysisLease(Guid id) => conversationGate.Enter(id);

    public async Task<string> InventoryAsync(Guid id, Guid? batch, CancellationToken ct)
    {
        await RequireConversationAsync(id, ct);
        return ProjectInventory.Tree(InBatch(await MetadataAsync(database, scope, id, ct), batch));
    }

    public Task<DocumentAnalysis?> GetAsync(Guid id, Guid analysisId, CancellationToken ct) =>
        database.Set<DocumentAnalysis>().AsNoTracking().SingleOrDefaultAsync(a => a.Id == analysisId && a.ConversationId == id, ct);

    public async Task DeleteRunAsync(Guid id, Guid runId, CancellationToken ct)
    {
        using var lease = AcquireAnalysisLease(id);
        await RequireConversationAsync(id, ct);
        new AnalysisCheckpoints(reader.Storage).DeleteRun(id, runId);
    }

    public async Task<ConversationSummary?> SelectAsync(Guid id, Guid analysisId, int revision, bool useInContext, CancellationToken ct)
    {
        using var lease = AcquireAnalysisLease(id);
        await RequireRevisionAsync(id, revision, ct);
        var analysis = await database.Set<DocumentAnalysis>().SingleOrDefaultAsync(a => a.Id == analysisId && a.ConversationId == id, ct);
        if (analysis is null) return null;
        if (useInContext)
        {
            var files = await MetadataAsync(database, scope, id, ct);
            if (ProjectInventory.Fingerprint(InBatch(files, analysis.ScopeBatchId)) != analysis.SourceFingerprint)
                throw new ArgumentException("Cette analyse est périmée : les sources ou exclusions ont changé. Relancez l’analyse.");
            foreach (var previous in await database.Set<DocumentAnalysis>().Where(a => a.ConversationId == id && a.UseInContext).ToListAsync(ct))
                previous.UseInContext = false;
        }
        analysis.UseInContext = useInContext;
        var summary = await conversations.StageAdvanceAsync(id, revision, ct);
        await database.SaveVersionedAsync(ct);
        return summary;
    }

    public async Task<bool> DeleteAsync(Guid id, Guid analysisId, int revision, CancellationToken ct)
    {
        using var lease = AcquireAnalysisLease(id);
        await RequireRevisionAsync(id, revision, ct);
        var analysis = await database.Set<DocumentAnalysis>().SingleOrDefaultAsync(a => a.Id == analysisId && a.ConversationId == id, ct);
        if (analysis is null) return false;
        database.Set<DocumentAnalysis>().Remove(analysis);
        await conversations.StageAdvanceAsync(id, revision, ct);
        await database.SaveVersionedAsync(ct);
        return true;
    }

    private Task<AnalysisConversation> RequireConversationAsync(Guid id, CancellationToken ct) =>
        conversations.RequireAsync(id, null, ct);

    private Task<AnalysisConversation> RequireRevisionAsync(Guid id, int revision, CancellationToken ct) =>
        conversations.RequireAsync(id, revision, ct);

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
        var metadata = await MetadataAsync(database, scope, id, ct);
        var reports = await database.Set<DocumentAnalysis>().AsNoTracking().Where(a => a.ConversationId == id)
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

    public static async Task<InventoryFile[]> MetadataAsync(DbContext database, DocumentScope scope, Guid id, CancellationToken ct) =>
        await scope.ReadAsync(database, id, false, true, q => q
            .Select(DocumentInventory.Projection).ToArrayAsync(ct), ct);
}
