using System.Text.Json;
using Alycia.Modules.Chat.Application;
using Alycia.Modules.Chat.Domain;
using Microsoft.EntityFrameworkCore;

namespace Alycia.Modules.Chat.Infrastructure;

public sealed record AnalysisItem(Guid Id, string Title, DateTimeOffset CreatedAtUtc, string Model,
    int FileCount, int SummaryCalls, bool UseInContext, bool IsStale, Guid? ScopeBatchId);

public sealed class DocumentAnalysisService(ChatDbContext database, IChatModel model, ChatOptions defaults, ProjectSourceReader reader)
{
    public async Task<DocumentAnalysis> AnalyzeAsync(Guid id, int revision, Guid? batch, int budget,
        Func<string, Task>? progress, CancellationToken ct, Guid? resumeRunId = null)
    {
        if (budget is < 512 or > 16384) throw new ArgumentException("Budget de synthèse attendu : 512 à 16384 octets UTF-8 estimés.");
        var conversation = await database.Conversations.SingleOrDefaultAsync(c => c.Id == id, ct)
            ?? throw new ConversationNotFoundException();
        ConversationConfiguration.CheckRevision(conversation, revision);
        if (resumeRunId is { } resumed)
        {
            var saved = await database.DocumentAnalyses.Where(a => a.ConversationId == id && a.ManifestJson.Contains(resumed.ToString())).ToArrayAsync(ct);
            foreach (var existing in saved)
            {
                using var manifest = JsonDocument.Parse(existing.ManifestJson);
                if (manifest.RootElement.TryGetProperty("analysisRunId", out var recorded) && recorded.GetGuid() == resumed)
                    return existing; // The database committed before the last checkpoint or client acknowledgement.
            }
        }
        if (await database.DocumentAnalyses.CountAsync(a => a.ConversationId == id, ct) >= 50)
            throw new ArgumentException("Limite de 50 analyses : supprimez une ancienne analyse avant de continuer.");
        var files = await database.ForConversation(id).Where(f => (!batch.HasValue || f.BatchId == batch)).OrderBy(f => f.Name).ThenBy(f => f.Id).ToArrayAsync(ct);
        if (files.Length == 0) throw new ArgumentException("Aucun fichier dans ce périmètre. Importez un dossier ou une archive.");
        var options = await model.PrepareOptionsAsync(conversation.Model, ConversationConfiguration.Options(conversation, defaults), ct);
        var measured = new MeteredChatModel(model);
        var eligible = files.Where(f => f.Text.Length > 0 && f.ContextMode != "excluded").ToArray();
        if (eligible.Length + 2 > options.SummaryMaxCalls)
            throw new ArgumentException("Le nombre de fichiers dépasse le budget d’appels de résumé. Choisissez un lot plus petit ou augmentez ce budget dans Réglages.");
        var inventory = ProjectInventory.Describe(files);
        var tree = ProjectInventory.Tree(inventory);
        var fingerprint = ProjectInventory.Fingerprint(inventory);
        var checkpoints = new AnalysisCheckpoints(reader.Storage);
        var identity = options.ModelIdentity ?? conversation.Model + ":identity-unavailable";
        var run = await checkpoints.StartAsync(id, batch, fingerprint, identity, budget, resumeRunId, ct);
        try
        {
            var reducer = new RecursiveSummarizer(measured, conversation.Model, options, progress);
            var leaves = new List<SummarySource>();
            if (progress is not null) await progress("Lecture de tree.txt par fragments · " + files.Length + " fichiers inventoriés");
            var overviewKey = AnalysisCheckpoints.UnitKey(Guid.Empty, fingerprint, "tree.txt", "inventory", identity, budget, options);
            var overviewCache = options.ModelIdentity is null ? null : await checkpoints.ReadUnitAsync(id, overviewKey, ct);
            var overview = overviewCache?.Summary ?? await reducer.ReduceAsync([new("tree.txt intégral", tree)], Math.Min(2048, budget), ct);
            if (overviewCache is null) await checkpoints.SaveUnitAsync(id, new(overviewKey, overview, true, "inventory", tree.Length, System.Text.Encoding.UTF8.GetByteCount(tree)), ct);
            leaves.Add(new("Inventaire tree.txt", overview));
            var fileSummaries = new List<object>();
            var complete = 0; var partial = 0; var codeSummarized = 0;
            var completeness = new Dictionary<Guid, bool>();
            foreach (var file in files)
            {
                ct.ThrowIfCancellationRequested();
                var metadata = inventory.Single(item => item.Id == file.Id);
                if (file.Text.Length == 0 || file.ContextMode == "excluded")
                {
                    fileSummaries.Add(new
                    {
                        file.Id,
                        path = file.Name,
                        summarized = false,
                        reason = file.Status,
                        excluded = file.ContextMode == "excluded"
                    });
                    continue;
                }
                if (progress is not null) await progress("Résumé du fichier : " + file.Name);
                var source = await reader.ReadAsync(file, ct);
                var isCode = ProjectContext.IsCode(file.Name);
                var key = AnalysisCheckpoints.UnitKey(file.Id, file.Sha256, file.Name, file.Status, identity, budget, options);
                // An unknown digest cannot establish cache compatibility across model calls.
                var cached = options.ModelIdentity is null ? null : await checkpoints.ReadUnitAsync(id, key, ct);
                string summary;
                if (cached is not null)
                {
                    summary = cached.Summary;
                    run = run with { ReusedUnits = run.ReusedUnits + 1 };
                    if (progress is not null) await progress("Réutilisation vérifiée : " + file.Name);
                }
                else
                {
                    summary = await reducer.ReduceAsync([new(file.Name,
                ProjectInventory.Metadata(metadata) + (isCode
                    ? "\nCode : relever symboles, responsabilités, dépendances, flux et tests observés. Distinguer faits et hypothèses."
                    : "\nDocument : relever les faits utiles au projet.")
                + "\nOrigine : " + source.Origin + "\nContenu :\n" + source.Text)], Math.Min(1024, budget), ct);

                    await checkpoints.SaveUnitAsync(id, new(key, summary, source.Complete, source.Origin,
                        source.Text.Length, System.Text.Encoding.UTF8.GetByteCount(source.Text)), ct);
                }
                run = run with { CompletedUnits = run.CompletedUnits + 1 };
                await checkpoints.SaveRunAsync(run, ct);
                if (source.Complete) complete++; else partial++;
                completeness[file.Id] = source.Complete;
                if (isCode) codeSummarized++;
                leaves.Add(new(file.Name, summary));
                fileSummaries.Add(new
                {
                    file.Id,
                    path = file.Name,
                    summarized = true,
                    summary,
                    extraction = file.Status,
                    completeSource = source.Complete,
                    source = source.Origin,
                    code = isCode,
                    charactersRead = source.Text.Length,
                    utf8BytesRead = System.Text.Encoding.UTF8.GetByteCount(source.Text)
                });
            }
            if (progress is not null) await progress("Réduction hiérarchique des résumés de fichiers…");
            var coverageText = $"Couverture : {complete}/{eligible.Length} sources textuelles lues intégralement, {partial} extraction(s) partielle(s), "
                + $"{codeSummarized} fichier(s) de code analysé(s), {files.Count(f => f.ContextMode == "excluded")} fichier(s) exclu(s).\n\n";
            var final = await reducer.ReduceAsync(leaves, budget - System.Text.Encoding.UTF8.GetByteCount(coverageText), ct);
            var analysis = new DocumentAnalysis
            {
                ConversationId = id,
                ScopeBatchId = batch,
                Title = batch.HasValue ? "Synthèse d’un lot importé" : "Synthèse des documents",
                Model = conversation.Model,
                SourceFingerprint = fingerprint,
                TreeText = tree,
                SummaryText = coverageText + final,
                FileCount = files.Length,
                SummaryCalls = reducer.Calls,
                UseInContext = true
            };
            analysis.ManifestJson = JsonSerializer.Serialize(new
            {
                schemaVersion = 3,
                analysisRunId = run.Id,
                pipelineVersion = AnalysisCheckpoints.PipelineVersion,
                run.ReusedUnits,
                identity,
                state = "completed",
                analysis.Id,
                analysis.CreatedAtUtc,
                analysis.Model,
                analysis.SourceFingerprint,
                analysis.ScopeBatchId,
                analysis.SummaryCalls,
                targetBytes = budget,
                usage = measured.Snapshot(options),
                coverage = new
                {
                    inventoriedFiles = files.Length,
                    eligibleFiles = eligible.Length,
                    summarizedFiles = complete + partial,
                    fullyReadFiles = complete,
                    partialFiles = partial,
                    codeFiles = files.Count(f => ProjectContext.IsCode(f.Name)),
                    codeFilesSummarized = codeSummarized,
                    excludedFiles = files.Count(f => f.ContextMode == "excluded"),
                    noTextFiles = files.Count(f => f.Text.Length == 0 && f.ContextMode != "excluded"),
                    allEligibleTextRead = complete == eligible.Length,
                    byFormat = files.GroupBy(f => Path.GetExtension(f.Name).ToLowerInvariant()).Select(g => new
                    {
                        format = g.Key,
                        total = g.Count(),
                        complete = g.Count(f => completeness.TryGetValue(f.Id, out var value) && value),
                        partial = g.Count(f => completeness.TryGetValue(f.Id, out var value) && !value),
                        excluded = g.Count(f => f.ContextMode == "excluded"),
                        noText = g.Count(f => f.Text.Length == 0 && f.ContextMode != "excluded")
                    })
                },
                files = inventory,
                fileSummaries,
                inventorySummary = overview
            }, ConversationTransfer.Json);
            // One SaveChanges includes the revision, new analysis and context selection.
            foreach (var previous in await database.DocumentAnalyses.Where(a => a.ConversationId == id && a.UseInContext).ToListAsync(ct))
                previous.UseInContext = false;
            database.DocumentAnalyses.Add(analysis);
            conversation.AttachFiles();
            await new EfChatRepository(database).SaveAsync(ct);
            await checkpoints.SaveRunAsync(run with { State = "completed", AnalysisId = analysis.Id }, CancellationToken.None);
            return analysis;
        }
        catch (Exception error)
        {
            var state = error is OperationCanceledException ? "cancelled" : run.CompletedUnits > 0 ? "partial" : "failed";
            try { await checkpoints.SaveRunAsync(run with { State = state, FailedUnits = error is OperationCanceledException ? 0 : 1 }, CancellationToken.None); }
            catch (IOException) { /* Validated unit files remain independently resumable. */ }
            throw;
        }
    }

    public async Task<AnalysisRun[]> RunsAsync(Guid id, CancellationToken ct)
    {
        if (!await database.Conversations.AnyAsync(c => c.Id == id, ct)) throw new ConversationNotFoundException();
        return await new AnalysisCheckpoints(reader.Storage).ListAsync(id, ct);
    }

    public async Task<IReadOnlyList<AnalysisItem>> ListAsync(Guid id, CancellationToken ct)
    {
        if (!await database.Conversations.AnyAsync(c => c.Id == id, ct)) throw new ConversationNotFoundException();
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
            a.UseInContext, ProjectInventory.Fingerprint(metadata.Where(f => !a.ScopeBatchId.HasValue || f.BatchId == a.ScopeBatchId))
                != a.SourceFingerprint, a.ScopeBatchId)).ToArray();
    }

    public static async Task<InventoryFile[]> MetadataAsync(ChatDbContext database, Guid id, CancellationToken ct) =>
        await database.ForConversation(id)
            .Select(f => new InventoryFile(f.Id, f.BatchId, f.ArchiveId, f.Name, f.Size, f.Sha256,
                Path.GetExtension(f.Name), f.SourceCreatedAt, f.SourceModifiedAt, f.MetadataSource,
                f.CreatedAtUtc, f.Status, f.ContextMode)).ToArrayAsync(ct);
}
