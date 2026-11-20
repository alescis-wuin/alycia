using System;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Alycia.Modules.Chat.Application;
using Avalonia.Controls;
using Avalonia.Threading;

namespace Alycia.Client.Avalonia;

public sealed partial class ChatApiClient
{
    public Task<EmbeddingProgress[]> IndexingAsync(Guid owner, bool project, CancellationToken ct) =>
        SendAsync<EmbeddingProgress[]>(HttpMethod.Get, $"/indexing?owner={owner}&project={project.ToString().ToLowerInvariant()}", null, ct);
    public Task QueueIndexingAsync(Guid owner, bool project, string? model, CancellationToken ct, int? batchSize = null, int? chunkCharacters = null,
        bool? hierarchical = null, string? documentPrefix = null, string? queryPrefix = null, EnrichmentOptions? enrichment = null) =>
        SendAsync<object>(HttpMethod.Post, "/indexing", new { owner, project, model, batchSize, chunkCharacters, hierarchical, documentPrefix, queryPrefix, enrichment }, ct);
    public Task PauseIndexingAsync(Guid owner, bool project, CancellationToken ct) =>
        SendAsync<object>(HttpMethod.Post, "/indexing/pause", new { owner, project }, ct);
}

public partial class ChatWindow
{
    private readonly DispatcherTimer _indexingTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private bool _checkingIndex;
    private Guid? _indexingOwner;
    private bool _indexingProject;

    private void InitializeIndexing()
    {
        _indexingTimer.Tick += async (_, _) => await RefreshIndexingAsync();
        _indexingTimer.Start();
        Closed += (_, _) => _indexingTimer.Stop();
    }

    private async Task RefreshIndexingAsync()
    {
        if (_checkingIndex || _lifetime.IsCancellationRequested) return;
        var owner = _indexingOwner ?? _current?.Id ?? _project.Id;
        var project = _indexingOwner.HasValue ? _indexingProject : _current is null;
        var label = C<TextBlock>("IndexingStatusText");
        var bar = C<ProgressBar>("IndexingProgressBar");
        if (owner is null) { label.IsVisible = bar.IsVisible = false; return; }
        _checkingIndex = true;
        try
        {
            var jobs = await _client.IndexingAsync(owner.Value, project, _lifetime.Token);
            if (owner != (_indexingOwner ?? _current?.Id ?? _project.Id)) return;
            var job = jobs.FirstOrDefault();
            if (job is null) { label.IsVisible = bar.IsVisible = false; return; }
            var running = job.State is "queued" or "running" or "partial";
            label.Text = job.State switch
            {
                "queued" => "Indexation en attente…",
                "partial" => $"Indexation partielle · {job.Detail}",
                "running" => $"Indexation en cours · {job.Completed}/{job.Total} sources · {job.Detail}",
                "failed" => "Indexation interrompue · " + job.Detail + " · Relance disponible dans Documents.",
                "paused" => "Indexation en pause · les lots terminés sont conservés.",
                _ => job.Detail
            };
            if (job.Enrichment is { Enabled: true } limits && job.State is "running" or "partial" or "completed")
                label.Text += $" · dernier cycle {job.CycleCalls}/{limits.JobCalls} appels";
            label.IsVisible = true; bar.IsVisible = running;
            bar.IsIndeterminate = job.State == "queued" || job.Total == 0;
            bar.Value = job.Total == 0 ? 0 : 100.0 * job.Completed / job.Total;
        }
        catch (OperationCanceledException) { }
        catch (ChatApiException) { label.Text = "État de l’indexation indisponible."; label.IsVisible = true; bar.IsVisible = false; }
        finally { _checkingIndex = false; }
    }

    private async Task QueueIndexingAsync(Guid owner, bool project)
    {
        EnrichmentOptions? enrichment = null;
        string? model = null; bool? hierarchical = null; string? documentPrefix = null, queryPrefix = null;
        int? batchSize = null; int? chunkCharacters = null;
        if (project)
        {
            var existing = (await _client.IndexingAsync(owner, true, _lifetime.Token)).FirstOrDefault();
            var fields = new KnowledgeFields(existing?.Enrichment ?? new());
            var input = Input(existing?.Model ?? "", 200);
            var batch = Input((existing?.BatchSize ?? 16).ToString(), 2);
            var chunk = Input((existing?.ChunkCharacters ?? 1600).ToString(), 4);
            var enriched = new CheckBox { Content = "Indexer les sections parentes et les symboles", IsChecked = existing?.Hierarchical ?? false };
            var doc = Input(existing?.DocumentPrefix ?? "", 256); var query = Input(existing?.QueryPrefix ?? "", 256);
            if (!await EditDialogAsync("Indexer les documents du projet", new StackPanel
            {
                Spacing = 10,
                Children = { Label("Nom exact du modèle d’embeddings installé dans Ollama"), input,
                    Label("Passages par lot · 1 à 64"), batch, Label("Caractères par passage · 128 à 3200"), chunk, enriched,
                    Label("Préfixe des documents"), doc, Label("Préfixe des requêtes"), query, fields.Panel }
            },
                "Démarrer", () =>
                {
                    try { enrichment = fields.Read(); } catch (ArgumentException error) { return error.Message; }
                    return !enrichment.Enabled && string.IsNullOrWhiteSpace(input.Text) ? "Indiquez un modèle local d’embeddings."
                    : !int.TryParse(batch.Text, out var b) || b is < 1 or > 64 || !int.TryParse(chunk.Text, out var c) || c is < 128 or > 3200
                        ? "Vérifiez la taille des lots et des passages." : null;
                })) return;
            model = input.Text?.Trim() ?? ""; hierarchical = enriched.IsChecked == true; documentPrefix = doc.Text; queryPrefix = query.Text;
            batchSize = int.Parse(batch.Text!); chunkCharacters = int.Parse(chunk.Text!);
        }
        await _client.QueueIndexingAsync(owner, project, model, _lifetime.Token, batchSize, chunkCharacters, hierarchical, documentPrefix, queryPrefix, enrichment);
        _indexingOwner = owner; _indexingProject = project;
        await RefreshIndexingAsync();
    }
}
