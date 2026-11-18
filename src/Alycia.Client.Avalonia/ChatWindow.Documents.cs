using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Platform.Storage;

namespace Alycia.Client.Avalonia;

public partial class ChatWindow
{
    private static Button ActionButtonInline(string label, Action action)
    {
        var button = new Button { Content = label };
        button.Click += (_, _) => action();
        return button;
    }

    private async Task ShowDocumentsAsync(bool project)
    {
        if ((project ? _project.Id : _current?.Id) is not Guid owner)
        {
            ShowPage("Documents").Children.Add(Label("Ouvrez une conversation ou choisissez un projet."));
            return;
        }
        var ct = _lifetime.Token;
        var profile = project ? await _client.ProjectAsync(owner, ct) : null;
        var chat = project ? null : (await _client.SettingsAsync(owner, ct)).Conversation;
        var files = project ? await _client.ProjectFilesAsync(owner, ct) : await _client.FilesAsync(owner, ct);
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
        imports.IsEnabled = profile?.Archived != true;
        body.Children.Add(imports);
        var filter = new TextBox { PlaceholderText = "Filtrer les documents" };
        var list = new ListBox { ItemsSource = files, MaxHeight = 300, SelectionMode = SelectionMode.Multiple };
        var details = new StackPanel { Spacing = 8 };
        body.Children.Add(filter);
        body.Children.Add(list);
        if (files.Length == 0) body.Children.Add(Label("Aucun document dans cette portée."));
        body.Children.Add(details);
        filter.TextChanged += (_, _) => list.ItemsSource = files.Where(f => f.Name.Contains(filter.Text ?? "", StringComparison.OrdinalIgnoreCase)).ToArray();
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
                    var source = await _client.SourceAsync(owner, project, file.Id, ct);
                    preview.Text = source.Text.Length == 0 ? "Aucun texte extrait." : source.Text;
                }));
                details.Children.Add(preview);
                details.Children.Add(new Expander { Header = "Version et taille", Content = Label($"{file.Size:N0} octets\nSHA-256 {file.Sha256}") });
            }
            if (profile?.Archived == true) return;
            var modes = new WrapPanel();
            foreach (var (title, mode) in new[] { ("Disponible", "project"), ("Épingler", "pinned"), ("Exclure", "excluded") })
                modes.Children.Add(ActionButton(title, async () =>
                {
                    int revision = profile?.Revision ?? chat!.Revision;
                    foreach (var selected in selection)
                    {
                        if (profile is not null) await _client.SetProjectFileModeAsync(profile with { Revision = revision }, selected.Id, mode, ct);
                        else await _client.ConfigureFileAsync(owner, selected.Id, revision, mode, ct);
                        revision++;
                    }
                    if (chat is not null) _current = (await _client.SettingsAsync(owner, ct)).Conversation;
                    await ShowDocumentsAsync(project);
                }));
            details.Children.Add(modes);
            if (chat is not null && selection.All(s => s.WorkspaceId.HasValue))
                details.Children.Add(ActionButton("Reprendre les réglages du projet", async () =>
                {
                    var current = chat;
                    foreach (var selected in selection)
                    {
                        await _client.RestoreFileModeAsync(current, selected.Id, ct);
                        current = (await _client.SettingsAsync(owner, ct)).Conversation;
                    }
                    _current = current;
                    await ShowDocumentsAsync(false);
                }));
            if (chat?.WorkspaceId is not null && selection.All(s => s.WorkspaceId is null && s.ContextMode != "excluded"))
                details.Children.Add(ActionButton("Partager avec le projet…", async () =>
                {
                    if (!await EditDialogAsync("Partager les documents", Label($"{selection.Length} document(s) deviendront disponibles aux autres conversations du projet."), "Partager")) return;
                    foreach (var selected in selection) await _client.ShareFileAsync(owner, selected.Id, ct);
                    await ShowDocumentsAsync(true);
                }));
            if (profile is not null && selection is [var removed])
                details.Children.Add(ActionButton("Retirer du projet…", async () =>
                {
                    if (!await EditDialogAsync("Retirer du projet", Label($"« {removed.Name} » et ses entrées d’archive sortiront du contexte des prochains échanges. Les réponses passées et les originaux sur disque sont conservés."), "Retirer")) return;
                    await _client.RemoveProjectFileAsync(profile, removed.Id, ct);
                    await ShowDocumentsAsync(true);
                }));
        };
        if (_current is not null) body.Children.Add(ActionButton("Analyses de cette conversation", AnalysesAsync));
    }
}
