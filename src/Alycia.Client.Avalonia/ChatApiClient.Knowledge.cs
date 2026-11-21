using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Alycia.Contracts.Chat;

namespace Alycia.Client.Avalonia;

public sealed partial class ChatApiClient
{
    public Task<IndexedNote[]> KnowledgeHistoryAsync(Guid owner, int before, string? group, CancellationToken ct)
        => SendAsync<IndexedNote[]>(HttpMethod.Get, $"/conversations/{owner}/index/knowledge/history?before={before}" + (group is null ? "" : "&group=" + Uri.EscapeDataString(group)), null, ct);
    public Task<IndexedNote> KnowledgeAsync(Guid owner, bool project, Guid file, string? unit, CancellationToken ct)
        => SendAsync<IndexedNote>(HttpMethod.Get, $"/indexing/knowledge?owner={owner}&project={project}&file={file}" + (unit is null ? "" : "&unit=" + Uri.EscapeDataString(unit)), null, ct);

    public Task<EmbeddingProgress[]> IndexingAsync(Guid owner, bool project, CancellationToken ct) =>
        SendAsync<EmbeddingProgress[]>(HttpMethod.Get, $"/indexing?owner={owner}&project={project.ToString().ToLowerInvariant()}", null, ct);
    public Task QueueIndexingAsync(Guid owner, bool project, string? model, CancellationToken ct, int? batchSize = null, int? chunkCharacters = null,
        bool? hierarchical = null, string? documentPrefix = null, string? queryPrefix = null, EnrichmentOptions? enrichment = null) =>
        SendAsync<object>(HttpMethod.Post, "/indexing", new { owner, project, model, batchSize, chunkCharacters, hierarchical, documentPrefix, queryPrefix, enrichment }, ct);
    public Task PauseIndexingAsync(Guid owner, bool project, CancellationToken ct) =>
        SendAsync<object>(HttpMethod.Post, "/indexing/pause", new { owner, project }, ct);
}
