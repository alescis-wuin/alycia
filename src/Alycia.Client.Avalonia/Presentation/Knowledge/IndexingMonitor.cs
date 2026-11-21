using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Alycia.Contracts.Chat;

namespace Alycia.Client.Avalonia;

internal sealed record IndexScope(Guid Owner, bool Project);
internal sealed record IndexingStatus(string? Text, bool Running = false, bool Indeterminate = false, double Percent = 0)
{
    internal static IndexingStatus From(EmbeddingProgress? job)
    {
        if (job is null) return new(Text: null);
        var text = job.State switch
        {
            "queued" => "Indexation en attente…",
            "partial" => $"Indexation partielle · {job.Detail}",
            "running" => $"Indexation en cours · {job.Completed}/{job.Total} sources · {job.Detail}",
            "failed" => "Indexation interrompue · " + job.Detail + " · Relance disponible dans Documents.",
            "paused" => "Indexation en pause · les lots terminés sont conservés.",
            _ => job.Detail
        };
        if (job.Enrichment is { Enabled: true } limits && job.State is "running" or "partial" or "completed")
            text += $" · dernier cycle {job.CycleCalls}/{limits.JobCalls} appels";
        return new(text, job.State is "queued" or "running" or "partial", job.State == "queued" || job.Total == 0,
            job.Total == 0 ? 0 : 100.0 * job.Completed / job.Total);
    }
}

internal sealed record ProjectIndexSettings(string Model, int BatchSize, int ChunkCharacters, bool Hierarchical,
    string? DocumentPrefix, string? QueryPrefix, EnrichmentOptions? Enrichment);

internal sealed class IndexingMonitor(ChatApiClient client)
{
    private readonly object _gate = new();
    private IndexScope? _selected;
    private CancellationTokenSource? _active;

    internal bool Select(IndexScope? next)
    {
        lock (_gate)
        {
            if (next == _selected) return false;
            _selected = next;
            _active?.Cancel();
            return true;
        }
    }

    internal async Task PollAsync(Action<IndexingStatus> publish, CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
        try
        {
            while (await timer.WaitForNextTickAsync(ct))
                await RefreshAsync(publish, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
    }

    internal async Task RefreshAsync(Action<IndexingStatus> publish, CancellationToken ct)
    {
        if (ct.IsCancellationRequested) return;
        IndexScope? scope;
        CancellationTokenSource request;
        lock (_gate)
        {
            if (_active is not null) return;
            scope = _selected;
            if (scope is null) { publish(new(Text: null)); return; }
            _active = request = CancellationTokenSource.CreateLinkedTokenSource(ct);
        }
        try
        {
            IndexingStatus status;
            try { status = IndexingStatus.From(await FirstJobAsync(scope.Owner, scope.Project, request.Token)); }
            catch (ChatApiException) { status = new("État de l’indexation indisponible."); }
            lock (_gate)
            {
                if (!request.IsCancellationRequested && scope == _selected) publish(status);
            }
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested) { }
        finally
        {
            lock (_gate) { _active = null; request.Dispose(); }
        }
    }

    internal async Task<EmbeddingProgress?> FirstJobAsync(Guid owner, bool project, CancellationToken ct) =>
        (await client.IndexingAsync(owner, project, ct)).FirstOrDefault();

    internal Task PauseAsync(Guid owner, bool project, CancellationToken ct) => client.PauseIndexingAsync(owner, project, ct);

    internal Task QueueAsync(Guid owner, bool project, ProjectIndexSettings? settings, CancellationToken ct) =>
        client.QueueIndexingAsync(owner, project, settings?.Model, ct, settings?.BatchSize, settings?.ChunkCharacters,
            settings?.Hierarchical, settings?.DocumentPrefix, settings?.QueryPrefix, settings?.Enrichment);
}
