using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Alycia.Modules.Chat.Application;

namespace Alycia.Client.Avalonia;

public sealed record AnalysisItem(Guid Id, string Title, DateTimeOffset CreatedAtUtc, string Model,
    int FileCount, int SummaryCalls, bool UseInContext, bool IsStale, Guid? ScopeBatchId)
{
    public override string ToString() => $"{CreatedAtUtc.ToLocalTime():dd/MM HH:mm} · {FileCount} fichiers · "
        + (IsStale ? "À recalculer" : UseInContext ? "Dans le contexte" : "Archivée");
}
public sealed record AnalysisCreated(Guid Id, string Title, int FileCount, int SummaryCalls);
public sealed record AnalysisDetails(Guid Id, string Title, string Model, int FileCount, int SummaryCalls,
    string TreeText, string SummaryText, string ManifestJson, string SourceFingerprint);

public sealed partial class ChatApiClient
{
    public Task<ContextPreview> PrepareMemoryAsync(Guid id, int revision, string content,
        Func<ModelDelta, Task> progress, CancellationToken ct) => StreamOperationAsync<ContextPreview>(
            $"/conversations/{id}/context/prepare", new { revision, content }, progress, ct);
    public Task<ConversationSummary> ResetMemoryAsync(Guid id, int revision, CancellationToken ct) =>
        SendAsync<ConversationSummary>(HttpMethod.Post, $"/conversations/{id}/memory/reset", new { revision }, ct);
    public Task<AnalysisItem[]> AnalysesAsync(Guid id, CancellationToken ct) =>
        SendAsync<AnalysisItem[]>(HttpMethod.Get, $"/conversations/{id}/analyses", null, ct);
    public Task<AnalysisDetails> AnalysisAsync(Guid id, Guid analysis, CancellationToken ct) =>
        SendAsync<AnalysisDetails>(HttpMethod.Get, $"/conversations/{id}/analyses/{analysis}", null, ct);
    public Task<AnalysisCreated> AnalyzeAsync(Guid id, int revision, Guid? batchId, int budget,
        Func<ModelDelta, Task> progress, CancellationToken ct) => StreamOperationAsync<AnalysisCreated>(
            $"/conversations/{id}/analyses", new { revision, batchId, budget }, progress, ct);
    public Task<byte[]> InventoryAsync(Guid id, Guid? batchId, CancellationToken ct) =>
        SendAsync<byte[]>(HttpMethod.Get, $"/conversations/{id}/inventory" + (batchId.HasValue ? "?batchId=" + batchId : ""), null, ct);
    public Task<byte[]> DownloadAnalysisAsync(Guid id, Guid analysis, string format, CancellationToken ct) =>
        SendAsync<byte[]>(HttpMethod.Get, $"/conversations/{id}/analyses/{analysis}?format={format}", null, ct);
    public Task<ConversationSummary> SelectAnalysisAsync(Guid id, Guid analysis, int revision, bool useInContext, CancellationToken ct) =>
        SendAsync<ConversationSummary>(HttpMethod.Patch, $"/conversations/{id}/analyses/{analysis}", new { revision, useInContext }, ct);
    public Task DeleteAnalysisAsync(Guid id, Guid analysis, int revision, CancellationToken ct) =>
        SendAsync<object>(HttpMethod.Delete, $"/conversations/{id}/analyses/{analysis}?revision={revision}", null, ct);
}
