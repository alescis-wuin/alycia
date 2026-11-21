using static Alycia.Client.Avalonia.DialogFields;
using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Platform.Storage;

namespace Alycia.Client.Avalonia;

public partial class ChatWindow
{

    private async Task ShowDocumentsAsync(bool project)
    {
        if ((project ? _project.Id : _current?.Id) is not Guid owner)
        {
            ShowPage("Documents").Children.Add(Label("Ouvrez une conversation ou choisissez un projet."));
            return;
        }
        FollowIndexScope(new(owner, project));
        var ct = _lifetime.Token;
        var documents = await DocumentList.LoadAsync(_client, owner, project, ct);
        var body = ShowPage(project ? "Documents du projet" : "Documents de la conversation");
        if (_project.Id.HasValue && _current is not null)
            body.Children.Add(ActionButton(project ? "Voir la portée de la conversation" : "Voir les documents du projet", () => ShowDocumentsAsync(!project)));
        var imports = new WrapPanel();
        var importFiles = ActionButton("Importer des fichiers ou archives", async () =>
            await ImportItemsAsync(await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { AllowMultiple = true }), project, true));
        var importFolder = ActionButton("Importer un dossier", async () =>
            await ImportItemsAsync(await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { AllowMultiple = true }), project, true));
        imports.Children.Add(importFiles);
        imports.Children.Add(importFolder);
        imports.IsEnabled = !documents.Archived;
        body.Children.Add(imports);
        body.Children.Add(ActionButton("Démarrer ou reprendre l’indexation", () => QueueIndexingAsync(owner, project)));
        body.Children.Add(ActionButton("Mettre l’indexation en pause", async () =>
        {
            await Indexing.PauseAsync(owner, project, ct);
            await RefreshIndexingAsync();
        }));
        var filter = new TextBox { PlaceholderText = "Filtrer les documents" };
        var list = new ListBox { ItemsSource = documents.Files, MaxHeight = 300, SelectionMode = SelectionMode.Multiple };
        var details = new StackPanel { Spacing = 8 };
        body.Children.Add(filter);
        body.Children.Add(list);
        if (documents.Files.Length == 0) body.Children.Add(Label("Aucun document dans cette portée."));
        body.Children.Add(details);
        filter.TextChanged += (_, _) => list.ItemsSource = documents.Filter(filter.Text);
        list.SelectionChanged += (_, _) =>
        {
            details.Children.Clear();
            var selection = list.SelectedItems?.OfType<FileItem>().ToArray() ?? [];
            if (selection.Length == 0) return;
            if (selection is [var file])
            {
                var scope = file.WorkspaceId.HasValue ? "Projet" : "Conversation";
                var mode = file.ContextMode switch { "pinned" => "Épinglé", "excluded" => "Exclu", "window" => "Fenêtre récente", _ => "Disponible" };
                details.Children.Add(Label($"{scope} · {mode} · {file.Status}"));
                var preview = new MarkdownView { IsMarkdown = false };
                if (file.ContextMode != "excluded") details.Children.Add(ActionButton("Voir l’aperçu", async () =>
                {
                    var source = await documents.PreviewAsync(file, ct);
                    preview.Text = source.Text.Length == 0 ? "Aucun texte extrait." : source.Text;
                }));
                details.Children.Add(preview);
                if (file.ContextMode != "excluded")
                {
                    var annotation = new StackPanel { Spacing = 8 };
                    details.Children.Add(ActionButton("Lire la synthèse indexée", () => ShowNoteAsync(annotation, owner, project, file.Id, null)));
                    details.Children.Add(annotation);
                }
                details.Children.Add(new Expander { Header = "Version et taille", Content = Label($"{file.Size:N0} octets\nSHA-256 {file.Sha256}") });
            }
            if (documents.Archived) return;
            var modes = new WrapPanel();
            foreach (var (title, mode) in new[] { ("Disponible", "project"), ("Épingler", "pinned"), ("Exclure", "excluded") })
                modes.Children.Add(ActionButton(title, async () =>
                {
                    var change = await documents.ChangeModesAsync(selection, mode, ct);
                    if (change.Conversation is not null) _current = change.Conversation;
                    await ShowDocumentsAsync(project);
                    if (change.Error is not null) Feedback($"{change.Completed}/{selection.Length} modifications enregistrées. {change.Error}", true);
                }));
            details.Children.Add(modes);
            if (documents.CanRestore(selection))
                details.Children.Add(ActionButton("Reprendre les réglages du projet", async () =>
                {
                    _current = await documents.RestoreAsync(selection, ct);
                    await ShowDocumentsAsync(false);
                }));
            if (documents.CanShare(selection))
                details.Children.Add(ActionButton("Partager avec le projet…", async () =>
                {
                    if (!await EditDialogAsync("Partager les documents", Label($"{selection.Length} document(s) deviendront disponibles aux autres conversations du projet."), "Partager")) return;
                    await documents.ShareAsync(selection, ct);
                    await ShowDocumentsAsync(true);
                }));
            if (project && selection is [var removed])
                details.Children.Add(ActionButton("Retirer du projet…", async () =>
                {
                    if (!await EditDialogAsync("Retirer du projet", Label($"« {removed.Name} » et ses entrées d’archive sortiront du contexte des prochains échanges. Les réponses passées et les originaux sur disque sont conservés."), "Retirer")) return;
                    await documents.RemoveAsync(removed, ct);
                    await ShowDocumentsAsync(true);
                }));
        };
        if (_current is not null) body.Children.Add(ActionButton("Analyses de cette conversation", AnalysesAsync));
    }
}
