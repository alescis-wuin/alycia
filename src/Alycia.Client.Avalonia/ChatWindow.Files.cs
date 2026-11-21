using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Platform.Storage;

namespace Alycia.Client.Avalonia;

public partial class ChatWindow
{
    private void InitializeFileControls()
    {
        C<Button>("UploadButton").Click += (_, _) =>
        {
            var menu = C<WrapPanel>("AttachmentActions");
            menu.IsVisible = !menu.IsVisible;
            if (menu.IsVisible) C<Button>("AttachFileCommand").Focus();
        };
        C<Button>("AttachFileCommand").Click += async (_, _) => await RunAsync(() => PickFilesAsync(false));
        C<Button>("AttachArchiveCommand").Click += async (_, _) => await RunAsync(() => PickFilesAsync(true));
        C<Button>("AttachProjectCommand").Click += async (_, _) => await RunAsync(() => ShowDocumentsAsync(true));
        C<Button>("FolderButton").Click += async (_, _) => await RunAsync(async () =>
        {
            var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Importer des dossiers",
                AllowMultiple = true
            });
            await ImportItemsAsync(folders);
        });
        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, (_, e) =>
        {
            e.DragEffects = !_busy && _current is not null && e.DataTransfer.TryGetFiles() is { Length: > 0 }
                ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        });
        AddHandler(DragDrop.DropEvent, async (_, e) =>
        {
            e.Handled = true;
            var files = e.DataTransfer.TryGetFiles();
            if (files is not null && _current is not null) await RunAsync(() => ImportItemsAsync(files));
        });
    }

    private async Task PickFilesAsync(bool archive)
    {
        var options = new FilePickerOpenOptions
        {
            Title = archive ? "Joindre une archive à la conversation" : "Joindre des fichiers à la conversation",
            AllowMultiple = true,
            FileTypeFilter = archive ? [new("Archives") { Patterns = ["*.zip", "*.tar", "*.tar.gz", "*.tgz", "*.gz"] }] : null
        };
        await ImportItemsAsync(await StorageProvider.OpenFilePickerAsync(options));
    }

    private async Task ImportItemsAsync(IEnumerable<IStorageItem> items, bool project = false, bool documents = false)
    {
        if ((project ? _project.Id : _current?.Id) is not Guid owner) return;
        var selected = items.ToArray();
        if (selected.Length == 0) return;
        var limits = await _client.ImportLimitsAsync(_lifetime.Token);
        var includeExcluded = false;
        if (selected.Any(item => item is IStorageFolder))
        {
            var choice = await ActionDialogAsync("Périmètre du dossier", new TextBlock
            {
                Text = "Inventaire complet : importer tous les fichiers réguliers du dossier, y compris les fichiers techniques et sensibles. "
                    + "Leurs originaux restent stockés localement et leurs métadonnées figurent dans tree.txt ; leur contenu reste exclu de l’IA.\n\n"
                    + "Sources utiles : ignorer avant import les chemins techniques ou sensibles habituels (.git, .venv, node_modules, .env…). "
                    + "L’arbre couvrira seulement les fichiers retenus.\n\n"
                    + $"Dans les deux cas : {limits.BatchEntries} fichiers / {limits.BatchMiB} Mio maximum, {limits.FileMiB} Mio par fichier et aucun lien symbolique. "
                    + "Choisissez des sous-dossiers si le projet dépasse ces limites.",
                TextWrapping = global::Avalonia.Media.TextWrapping.Wrap,
                FontSize = 15
            }, [("Inventaire complet", "complete"), ("Sources utiles", "filtered")]);
            if (choice is not ("complete" or "filtered")) { Feedback("Import annulé."); return; }
            includeExcluded = choice == "complete";
        }
        var result = await DocumentUpload.RunAsync(_client, owner, project, selected, limits, includeExcluded,
            count => Feedback($"Import de {count} fichier(s)…"), _lifetime.Token);
        if (result is null) { Feedback("Aucun fichier sélectionné (les dossiers vides ne sont pas importés)."); return; }
        if (_current is not null) await LoadAsync(_current);
        if (documents) await ShowDocumentsAsync(project);
        Feedback($"{result.Stored} fichier(s) enregistrés · {result.Skipped} élément(s) écarté(s) avant import · "
            + $"{result.Excluded} contenu(s) exclu(s) de l’IA. "
            + "Ouvrez Analyse du projet pour produire tree.txt et une synthèse récursive.");
    }

    private async void DownloadFileClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { CommandParameter: ChatMessageItem { AttachmentId: { } id } message }) return;
        await RunAsync(async () =>
        {
            var destination = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Enregistrer le fichier",
                SuggestedFileName = Path.GetFileName(message.Content.Split('\n')[0])
            });
            if (destination is null) { Feedback("Téléchargement annulé."); return; }
            await using var output = await destination.OpenWriteAsync();
            if (output.CanSeek) output.SetLength(0);
            await _client.DownloadAsync(message.ConversationId, id, output, _lifetime.Token);
            Feedback("Fichier téléchargé.");
        });
    }

    private async void RemoveFileClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { CommandParameter: ChatMessageItem { AttachmentId: { } id } message }) return;
        await RunAsync(async () =>
        {
            var dialog = new Window
            {
                Title = "Retirer le fichier ?",
                Width = 440,
                Height = 190,
                CanResize = false,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };
            var cancel = new Button { Content = "Conserver" };
            var remove = new Button { Content = "Retirer" };
            cancel.Click += (_, _) => dialog.Close(false);
            remove.Click += (_, _) => dialog.Close(true);
            dialog.Content = new StackPanel
            {
                Margin = new Thickness(24),
                Spacing = 20,
                Children =
            {
                new TextBlock { Text = "Le fichier et ses éventuelles entrées d’archive seront supprimés.", TextWrapping = global::Avalonia.Media.TextWrapping.Wrap },
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Children = { cancel, remove } }
            }
            };
            if (!await dialog.ShowDialog<bool>(this)) { Feedback("Suppression annulée."); return; }
            await _client.DeleteFileAsync(message.ConversationId, id, _lifetime.Token);
            if (_current is not null) await LoadAsync(_current);
            Feedback("Fichier retiré de la conversation et du contexte.");
        });
    }
}
