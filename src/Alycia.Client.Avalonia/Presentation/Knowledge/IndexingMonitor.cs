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
    internal IndexScope? Selected { get { lock (_gate) return _selected; } }

    internal void Select(Guid? owner, bool project)
    {
        var next = owner is Guid id ? new IndexScope(id, project) : null;
        lock (_gate)
        {
            if (next == _selected) return;
            _selected = next;
            _active?.Cancel();
        }
    }

    internal async Task PollAsync(Func<IndexScope?> fallback, Action<IndexingStatus> publish, CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
        try
        {
            while (await timer.WaitForNextTickAsync(ct))
                await RefreshAsync(fallback, publish, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
    }

    internal async Task RefreshAsync(Func<IndexScope?> fallback, Action<IndexingStatus> publish, CancellationToken ct)
    {
        if (ct.IsCancellationRequested) return;
        IndexScope? scope;
        CancellationTokenSource request;
        lock (_gate)
        {
            if (_active is not null) return;
            scope = _selected ?? fallback();
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
                if (!request.IsCancellationRequested && scope == (_selected ?? fallback())) publish(status);
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

    internal async Task QueueAsync(Guid owner, bool project, ProjectIndexSettings? settings, CancellationToken ct)
    {
        await client.QueueIndexingAsync(owner, project, settings?.Model, ct, settings?.BatchSize, settings?.ChunkCharacters,
            settings?.Hierarchical, settings?.DocumentPrefix, settings?.QueryPrefix, settings?.Enrichment);
        Select(owner, project);
    }
}
