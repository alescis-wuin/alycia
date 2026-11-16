using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Alycia.Modules.Chat.Application;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;

namespace Alycia.Client.Avalonia;

public partial class ChatWindow
{
    private async Task<string?> ActionDialogAsync(string title, Control content, (string Label, string Action)[] actions,
        Func<string, string?>? validate = null)
    {
        var dialog = new Window
        {
            Title = title,
            Width = 820,
            Height = 700,
            MinWidth = 600,
            MinHeight = 400,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        var buttons = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right };
        var error = new TextBlock { Foreground = Brush.Parse("#F1AABA"), TextWrapping = TextWrapping.Wrap, IsVisible = false };
        foreach (var action in actions.Append(("Fermer", "close")))
        {
            var button = new Button { Content = action.Item1, Margin = new Thickness(4), Padding = new Thickness(12, 8) };
            button.Click += (_, _) =>
            {
                error.Text = action.Item2 == "close" ? null : validate?.Invoke(action.Item2);
                error.IsVisible = error.Text is not null;
                if (!error.IsVisible) dialog.Close(action.Item2);
            };
            buttons.Children.Add(button);
        }
        var scroll = new ScrollViewer { Content = content, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        var grid = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto"), Margin = new Thickness(24), RowSpacing = 12 };
        grid.Children.Add(error); Grid.SetRow(scroll, 1); grid.Children.Add(scroll);
        Grid.SetRow(buttons, 2); grid.Children.Add(buttons); dialog.Content = grid;
        dialog.KeyDown += (_, e) => { if (e.Key == Key.Escape) dialog.Close("close"); };
        return await dialog.ShowDialog<string?>(this);
    }

    private async Task<T> WithProgressAsync<T>(string title, Func<Func<ModelDelta, Task>, CancellationToken, Task<T>> operation)
    {
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _generation = cancel; UpdateControls(); Feedback(title);
        try
        {
            return await operation(delta => { if (delta.Status is not null) Feedback(delta.Status); return Task.CompletedTask; }, cancel.Token);
        }
        finally { _generation = null; }
    }

    private async Task ReloadCurrentAsync()
    {
        if (_current is null) return;
        var id = _current.Id;
        await RefreshConversationsAsync(id);
        if (_current is not null) await LoadAsync(_current);
    }

    private async Task MemoryAsync()
    {
        if (_current is null) return;
        var settings = await _client.SettingsAsync(_current.Id, _lifetime.Token);
        var conversation = settings.Conversation;
        var draft = C<TextBox>("MessageInput").Text?.Trim() ?? "";
        ContextPreview? preview = null;
        string? previewError = null;
        if (draft.Length > 0)
        {
            try { preview = await _client.PreviewAsync(conversation.Id, draft, _lifetime.Token); }
            catch (ChatApiException exception) { previewError = exception.Message; }
        }
        var panel = new StackPanel { Spacing = 14 };
        panel.Children.Add(Label(settings.Options.AutoSummarize
            ? "Le système, les notes, les deux derniers échanges complets et votre demande sont protégés. Les échanges plus anciens sont résumés seulement si nécessaire."
            : "Le résumé automatique est désactivé. La fenêtre glissante simple est utilisée ; activez le résumé dans Réglages pour protéger les deux derniers échanges."));
        if (preview is not null)
        {
            panel.Children.Add(Label($"Entrée estimée : {preview.Context.EstimatedInputTokens} / "
                + $"{preview.Options.ContextTokens - preview.Options.MaxOutputTokens - preview.Options.SafetyTokens} · "
                + $"{preview.Context.HistoryMessagesIncluded} messages bruts · {preview.Context.FilesIncluded} documents"));
            panel.Children.Add(Label(preview.Context.NeedsSummary
                ? "Un résumé doit être préparé. Cet aperçu est incomplet ; l’envoi effectuera la réduction automatiquement."
                : "Le contexte courant respecte le budget estimé. L’aperçu ne déclenche aucune génération."));
        }
        else if (previewError is not null) panel.Children.Add(Label(previewError));
        else panel.Children.Add(Label("Saisissez votre prochaine demande dans la zone de message pour inspecter ou préparer son contexte."));
        panel.Children.Add(Label(conversation.MemoryThroughSequence > 0
            ? $"Mémoire enregistrée : messages 1 à {conversation.MemoryThroughSequence} · modèle {conversation.MemoryModel}\nEmpreinte de provenance : {conversation.MemorySourceHash}"
            : "Aucun résumé de conversation enregistré."));
        panel.Children.Add(new MarkdownView { Text = conversation.MemoryText.Length == 0 ? "Les messages originaux restent conservés." : conversation.MemoryText });
        var choice = await ActionDialogAsync("Mémoire et contexte", panel,
            [("Voir les messages préparés", "preview"), ("Préparer le résumé", "prepare"), ("Réinitialiser la mémoire", "reset")],
            action => action is "preview" or "prepare" && draft.Length == 0 ? "Saisissez d’abord votre demande dans la conversation."
                : action == "prepare" && !settings.Options.AutoSummarize ? "Activez le résumé automatique dans Réglages." : null);
        if (choice == "preview" && preview is not null)
            await ActionDialogAsync("Messages préparés — estimation UTF-8", new StackPanel
            {
                Spacing = 12,
                Children =
                { Label(preview.Context.NeedsSummary ? "Aperçu incomplet : réduction nécessaire avant envoi." : "Contexte préparé pour la demande courante."),
                    new MarkdownView { IsMarkdown = false, Text = string.Join("\n\n", preview.Context.Messages.Select(m => $"[{m.Role}]\n{m.Content}")) } }
            }, []);
        else if (choice == "prepare")
        {
            var result = await WithProgressAsync("Préparation de la mémoire…", (progress, ct) =>
                _client.PrepareMemoryAsync(conversation.Id, conversation.Revision, draft, progress, ct));
            await ReloadCurrentAsync();
            Feedback($"Mémoire prête · {result.Context.SummaryCalls} appel(s) · messages 1–{result.Context.SummaryThroughSequence} résumés.");
        }
        else if (choice == "reset")
        {
            await _client.ResetMemoryAsync(conversation.Id, conversation.Revision, _lifetime.Token);
            await ReloadCurrentAsync(); Feedback("Mémoire dérivée effacée. Les messages originaux sont conservés ; elle sera reconstruite au besoin.");
        }
    }

    private sealed record ScopeChoice(Guid? Id, string Label) { public override string ToString() => Label; }

    private async Task AnalysesAsync()
    {
        if (_current is null) return;
        var settings = await _client.SettingsAsync(_current.Id, _lifetime.Token);
        var conversation = settings.Conversation;
        var files = await _client.FilesAsync(conversation.Id, _lifetime.Token);
        var reports = await _client.AnalysesAsync(conversation.Id, _lifetime.Token);
        var scopes = new[] { new ScopeChoice(null, $"Tous les documents · {files.Length} fichiers") }
            .Concat(files.GroupBy(f => f.BatchId).Select(g => new ScopeChoice(g.Key,
                $"Import du {g.Min(f => f.CreatedAtUtc).ToLocalTime():dd/MM HH:mm} · {g.Count()} fichiers · {g.First().Name}"))).ToArray();
        var scope = new ComboBox { ItemsSource = scopes, SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
        var selected = new ComboBox
        {
            ItemsSource = reports,
            SelectedIndex = reports.Length > 0 ? 0 : -1,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            PlaceholderText = "Aucune analyse enregistrée"
        };
        var budget = Input("2048", 5);
        var panel = new StackPanel { Spacing = 12 };
        panel.Children.Add(Label("1. Importez un dossier ou ZIP depuis la conversation. 2. Choisissez le périmètre. 3. Générez la synthèse. Le modèle local reçoit tree.txt par fragments puis les contenus extractibles."));
        panel.Children.Add(scope);
        panel.Children.Add(Label("Budget de la synthèse finale (512 à 16384 octets UTF-8 estimés)")); panel.Children.Add(budget);
        panel.Children.Add(Label($"Budget d’appels : {settings.Options.SummaryMaxCalls}. L’opération peut être longue et reste annulable. "
            + "Les fichiers sans texte et les exclusions figurent dans le manifeste. Les dossiers exclus avant import ne sont pas parcourus."));
        panel.Children.Add(Label("Analyses enregistrées")); panel.Children.Add(selected);
        panel.Children.Add(Label("Une nouvelle synthèse remplace les extraits bruts de son périmètre dans le contexte. Une analyse périmée est ignorée automatiquement. Les originaux restent disponibles."));
        var action = await ActionDialogAsync("Synthèses de dossiers et archives", panel,
            [("Créer une synthèse", "create"), ("Télécharger tree.txt", "tree"), ("Ouvrir l’analyse", "view"),
             ("Activer / désactiver", "toggle"), ("Supprimer l’analyse", "delete")], key =>
            key == "create" && files.Length == 0 ? "Importez d’abord un dossier ou une archive."
            : key == "create" && (!int.TryParse(budget.Text, out var value) || value is < 512 or > 16384) ? "Budget attendu : 512 à 16384."
            : key is "view" or "toggle" or "delete" && selected.SelectedItem is null ? "Choisissez une analyse enregistrée." : null);
        if (action == "tree")
            await SaveBytesAsync("tree.txt", await _client.InventoryAsync(conversation.Id, ((ScopeChoice)scope.SelectedItem!).Id, _lifetime.Token));
        else if (action == "create")
        {
            var created = await WithProgressAsync("Analyse des documents…", (progress, ct) => _client.AnalyzeAsync(conversation.Id,
                conversation.Revision, ((ScopeChoice)scope.SelectedItem!).Id, int.Parse(budget.Text!), progress, ct));
            await ReloadCurrentAsync();
            Feedback($"Synthèse enregistrée · {created.FileCount} fichiers inventoriés · {created.SummaryCalls} appels au modèle.");
            await ShowAnalysisAsync(conversation.Id, created.Id);
        }
        else if (selected.SelectedItem is AnalysisItem report)
        {
            if (action == "view") await ShowAnalysisAsync(conversation.Id, report.Id);
            if (action == "toggle")
            {
                await _client.SelectAnalysisAsync(conversation.Id, report.Id, conversation.Revision, !report.UseInContext, _lifetime.Token);
                await ReloadCurrentAsync(); Feedback(report.UseInContext ? "Synthèse retirée du contexte." : "Synthèse sélectionnée pour le contexte.");
            }
            if (action == "delete" && await EditDialogAsync("Supprimer cette analyse ?",
                Label("Seuls le résumé et son inventaire dérivé seront supprimés. Les fichiers originaux restent conservés."), "Supprimer"))
            {
                await _client.DeleteAnalysisAsync(conversation.Id, report.Id, conversation.Revision, _lifetime.Token);
                await ReloadCurrentAsync(); Feedback("Analyse supprimée ; documents originaux conservés.");
            }
        }
    }

    private async Task ShowAnalysisAsync(Guid conversation, Guid id)
    {
        var details = await _client.AnalysisAsync(conversation, id, _lifetime.Token);
        var panel = new StackPanel
        {
            Spacing = 14,
            Children =
            { Label($"{details.Model} · {details.FileCount} fichiers · {details.SummaryCalls} appels\nSHA-256 du manifeste source : {details.SourceFingerprint}"),
                new MarkdownView { Text = details.SummaryText },
                new Expander { Header = "Inventaire complet tree.txt", Content = new MarkdownView { Text = details.TreeText, IsMarkdown = false } } }
        };
        var action = await ActionDialogAsync(details.Title, panel,
            [("Exporter la synthèse", "markdown"), ("Exporter tree.txt", "tree"), ("Exporter la provenance", "manifest")]);
        if (action is "markdown" or "tree" or "manifest")
            await SaveBytesAsync(action == "tree" ? "tree.txt" : action == "manifest" ? "analysis.json" : "summary.md",
                await _client.DownloadAnalysisAsync(conversation, id, action, _lifetime.Token));
    }

    private async Task SaveBytesAsync(string name, byte[] bytes)
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { Title = "Exporter", SuggestedFileName = name });
        if (file is null) return;
        await using var output = await file.OpenWriteAsync();
        if (output.CanSeek) output.SetLength(0);
        await output.WriteAsync(bytes, _lifetime.Token); Feedback("Fichier exporté.");
    }
}
