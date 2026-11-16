using System.Text;
using System.Text.Json;
using Alycia.Modules.Chat.Application;
using Alycia.Modules.Chat.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Alycia.Api.Chat;

internal static partial class ChatEndpoints
{
    private static void MapAnalysis(RouteGroupBuilder routes)
    {
        routes.MapGet("/conversations/{id:guid}/inventory", async (Guid id, Guid? batchId,
            ChatDbContext database, CancellationToken ct) =>
        {
            if (!await database.Conversations.AnyAsync(c => c.Id == id, ct)) throw new ConversationNotFoundException();
            var files = await DocumentAnalysisService.MetadataAsync(database, id, ct);
            return Results.File(Encoding.UTF8.GetBytes(ProjectInventory.Tree(files.Where(f => !batchId.HasValue || f.BatchId == batchId))),
                "text/plain; charset=utf-8", "tree.txt");
        });
        routes.MapGet("/conversations/{id:guid}/analyses", async (Guid id, DocumentAnalysisService service, CancellationToken ct) =>
            Results.Ok(await service.ListAsync(id, ct)));
        routes.MapGet("/conversations/{id:guid}/analyses/{analysisId:guid}", async (Guid id, Guid analysisId,
            string? format, ChatDbContext database, HttpContext http, CancellationToken ct) =>
        {
            var analysis = await database.DocumentAnalyses.AsNoTracking().SingleOrDefaultAsync(a => a.Id == analysisId && a.ConversationId == id, ct);
            if (analysis is null) return Results.NotFound();
            http.Response.Headers.CacheControl = "no-store";
            return format switch
            {
                "tree" => Results.File(Encoding.UTF8.GetBytes(analysis.TreeText), "text/plain; charset=utf-8", "tree.txt"),
                "markdown" => Results.File(Encoding.UTF8.GetBytes(analysis.SummaryText), "text/markdown; charset=utf-8", "summary.md"),
                "manifest" => Results.File(Encoding.UTF8.GetBytes(analysis.ManifestJson), "application/json", "analysis.json"),
                null => Results.Ok(analysis),
                _ => throw new ArgumentException("Format attendu : tree, markdown ou manifest.")
            };
        });
        routes.MapPost("/conversations/{id:guid}/analyses", async (Guid id, AnalyzeRequest request,
            DocumentAnalysisService service, HttpContext http, CancellationToken ct) =>
        {
            using var lease = GenerationGate.Shared.Enter(id);
            http.Response.ContentType = "application/x-ndjson; charset=utf-8";
            http.Response.Headers.CacheControl = "no-store";
            async Task WriteAsync(object item)
            {
                await http.Response.WriteAsync(JsonSerializer.Serialize(item, StreamJson) + "\n", ct);
                await http.Response.Body.FlushAsync(ct);
            }
            try
            {
                var report = await service.AnalyzeAsync(id, request.Revision, request.BatchId, request.Budget,
                    status => WriteAsync(new { type = "delta", content = "", status }), ct);
                await WriteAsync(new { type = "done", result = new { report.Id, report.Title, report.FileCount, report.SummaryCalls } });
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
            catch (Exception exception) when (http.Response.HasStarted && !ct.IsCancellationRequested)
            {
                await WriteAsync(new
                {
                    type = "error",
                    message = exception is ArgumentException or ChatModelException or ChatConflictException
                    ? exception.Message : "L’analyse n’a pas été confirmée. Rechargez la liste ; vos documents sont conservés."
                });
            }
            return Results.Empty;
        });
        routes.MapPatch("/conversations/{id:guid}/analyses/{analysisId:guid}", async (Guid id, Guid analysisId,
            SelectAnalysis request, ChatDbContext database, CancellationToken ct) =>
        {
            using var lease = GenerationGate.Shared.Enter(id);
            var conversation = await database.Conversations.SingleOrDefaultAsync(c => c.Id == id, ct)
                ?? throw new ConversationNotFoundException();
            ConversationConfiguration.CheckRevision(conversation, request.Revision);
            var analysis = await database.DocumentAnalyses.SingleOrDefaultAsync(a => a.Id == analysisId && a.ConversationId == id, ct);
            if (analysis is null) return Results.NotFound();
            if (request.UseInContext)
            {
                var files = await DocumentAnalysisService.MetadataAsync(database, id, ct);
                if (ProjectInventory.Fingerprint(files.Where(f => !analysis.ScopeBatchId.HasValue || f.BatchId == analysis.ScopeBatchId)) != analysis.SourceFingerprint)
                    throw new ArgumentException("Cette analyse est périmée : les sources ou exclusions ont changé. Relancez l’analyse.");
                foreach (var other in await database.DocumentAnalyses.Where(a => a.ConversationId == id && a.UseInContext).ToListAsync(ct))
                    other.UseInContext = false;
            }
            analysis.UseInContext = request.UseInContext;
            conversation.AttachFiles();
            await new EfChatRepository(database).SaveAsync(ct);
            return Results.Ok(conversation);
        });
        routes.MapDelete("/conversations/{id:guid}/analyses/{analysisId:guid}", async (Guid id, Guid analysisId,
            int revision, ChatDbContext database, CancellationToken ct) =>
        {
            using var lease = GenerationGate.Shared.Enter(id);
            var conversation = await database.Conversations.SingleOrDefaultAsync(c => c.Id == id, ct)
                ?? throw new ConversationNotFoundException();
            ConversationConfiguration.CheckRevision(conversation, revision);
            var report = await database.DocumentAnalyses.SingleOrDefaultAsync(a => a.Id == analysisId && a.ConversationId == id, ct);
            if (report is null) return Results.NotFound();
            database.DocumentAnalyses.Remove(report);
            conversation.AttachFiles();
            await new EfChatRepository(database).SaveAsync(ct);
            return Results.NoContent();
        });
    }

    private sealed record AnalyzeRequest(int Revision, Guid? BatchId, int Budget = 2048);
    private sealed record SelectAnalysis(int Revision, bool UseInContext);
}
