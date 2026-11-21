using System.Text;
using Alycia.Modules.Chat.Application;
using Alycia.Modules.Chat.Infrastructure;

namespace Alycia.Api.Chat;

internal static partial class ChatEndpoints
{
    private static void MapAnalysis(RouteGroupBuilder routes)
    {
        routes.MapGet("/conversations/{id:guid}/inventory", async (Guid id, Guid? batchId,
            DocumentAnalysisService service, CancellationToken ct) =>
            Results.File(Encoding.UTF8.GetBytes(await service.InventoryAsync(id, batchId, ct)), "text/plain; charset=utf-8", "tree.txt"));
        routes.MapGet("/conversations/{id:guid}/analyses", async (Guid id, DocumentAnalysisService service, CancellationToken ct) =>
            Results.Ok(await service.ListAsync(id, ct)));
        routes.MapGet("/conversations/{id:guid}/analysis-runs", async (Guid id, DocumentAnalysisService service, CancellationToken ct) =>
            Results.Ok(await service.RunsAsync(id, ct)));
        routes.MapDelete("/conversations/{id:guid}/analysis-runs/{runId:guid}", async (Guid id, Guid runId,
            DocumentAnalysisService service, CancellationToken ct) =>
        {
            await service.DeleteRunAsync(id, runId, ct);
            return Results.NoContent();
        });
        routes.MapGet("/conversations/{id:guid}/analyses/{analysisId:guid}", async (Guid id, Guid analysisId,
            string? format, DocumentAnalysisService service, HttpContext http, CancellationToken ct) =>
        {
            var analysis = await service.GetAsync(id, analysisId, ct);
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
            using var lease = service.AcquireAnalysisLease(id);
            http.Response.ContentType = "application/x-ndjson; charset=utf-8";
            http.Response.Headers.CacheControl = "no-store";
            Task WriteAsync(object item) => WriteEventAsync(http.Response, item, ct);
            try
            {
                var report = await service.AnalyzeAsync(id, request.Revision, request.BatchId, request.Budget,
                    status => WriteAsync(new { type = "delta", content = "", status }), ct, request.ResumeRunId);
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
            SelectAnalysis request, DocumentAnalysisService service, CancellationToken ct) =>
        {
            var conversation = await service.SelectAsync(id, analysisId, request.Revision, request.UseInContext, ct);
            return conversation is null ? Results.NotFound() : Results.Ok(conversation.ToResponse());
        });
        routes.MapDelete("/conversations/{id:guid}/analyses/{analysisId:guid}", async (Guid id, Guid analysisId,
            int revision, DocumentAnalysisService service, CancellationToken ct) =>
            await service.DeleteAsync(id, analysisId, revision, ct) ? Results.NoContent() : Results.NotFound());
    }

    private sealed record AnalyzeRequest(int Revision, Guid? BatchId, int Budget = 2048, Guid? ResumeRunId = null);
    private sealed record SelectAnalysis(int Revision, bool UseInContext);
}
