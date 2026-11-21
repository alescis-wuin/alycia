using static Alycia.Client.Avalonia.DialogFields;
using System;
using System.Threading.Tasks;
using Alycia.Contracts.Chat;
using Avalonia.Controls;

namespace Alycia.Client.Avalonia;

public partial class ChatWindow
{
    private IndexingMonitor? _indexing;
    private IndexingMonitor Indexing => _indexing ??= new(_client);

    private IndexScope? DefaultIndexScope() => _current is not null ? new(_current.Id, false)
        : _project.Id is Guid id ? new(id, true) : null;

    private void InitializeIndexing() => _ = Indexing.PollAsync(DefaultIndexScope, ShowIndexingStatus, _lifetime.Token);

    private Task RefreshIndexingAsync() => Indexing.RefreshAsync(DefaultIndexScope, ShowIndexingStatus, _lifetime.Token);

    private void ShowIndexingStatus(IndexingStatus status)
    {
        var label = C<TextBlock>("IndexingStatusText");
        var bar = C<ProgressBar>("IndexingProgressBar");
        label.Text = status.Text;
        label.IsVisible = status.Text is not null;
        bar.IsVisible = status.Running;
        bar.IsIndeterminate = status.Indeterminate;
        bar.Value = status.Percent;
    }

    private async Task QueueIndexingAsync(Guid owner, bool project)
    {
        EnrichmentOptions? enrichment = null;
        ProjectIndexSettings? settings = null;
        if (project)
        {
            var existing = await Indexing.FirstJobAsync(owner, true, _lifetime.Token);
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
            settings = new(input.Text?.Trim() ?? "", int.Parse(batch.Text!), int.Parse(chunk.Text!), enriched.IsChecked == true,
                doc.Text, query.Text, enrichment);
        }
        await Indexing.QueueAsync(owner, project, settings, _lifetime.Token);
        await RefreshIndexingAsync();
    }
}
