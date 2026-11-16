using System;
using Alycia.Modules.Chat.Application;
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
    private static ChatMessageItem[] Timeline(MessagePage page) => page.Messages
        .Concat((page.Files ?? []).Select(f => f.ToMessage(page.Conversation.Id)))
        .OrderBy(m => m.Sequence).ThenBy(m => m.IsFile ? 0 : 1).ThenBy(m => m.CreatedAtUtc).ToArray();

    private void InitializeFileControls()
    {
        C<Button>("UploadButton").Click += async (_, _) => await RunAsync(async () =>
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Importer des fichiers ou archives",
                AllowMultiple = true
            });
            await ImportItemsAsync(files);
        });
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

    private async Task ImportItemsAsync(IEnumerable<IStorageItem> items)
    {
        if (_current is null) return;
        var selected = items.ToArray();
        var includeExcluded = false;
        if (selected.Any(item => item is IStorageFolder))
        {
            var choice = await ActionDialogAsync("Périmètre du dossier", new TextBlock
            {
                Text = "Inventaire complet : importer tous les fichiers réguliers du dossier, y compris les fichiers techniques et sensibles. "
                    + "Leurs originaux restent stockés localement et leurs métadonnées figurent dans tree.txt ; leur contenu reste exclu de l’IA.\n\n"
                    + "Sources utiles : ignorer avant import les chemins techniques ou sensibles habituels (.git, .venv, node_modules, .env…). "
                    + "L’arbre couvrira seulement les fichiers retenus.\n\n"
                    + "Dans les deux cas : 500 fichiers / 100 Mio maximum, 25 Mio par fichier et aucun lien symbolique. "
                    + "Choisissez des sous-dossiers si le projet dépasse ces limites.",
                TextWrapping = global::Avalonia.Media.TextWrapping.Wrap,
                FontSize = 15
            }, [("Inventaire complet", "complete"), ("Sources utiles", "filtered")]);
            if (choice is not ("complete" or "filtered")) { Feedback("Import annulé."); return; }
            includeExcluded = choice == "complete";
        }
        var files = new List<UploadFile>();
        var excluded = 0;
        ulong totalBytes = 0;
        var seenFolders = new HashSet<string>(StringComparer.Ordinal);
        async Task VisitAsync(IStorageItem item, string prefix, int depth)
        {
            _lifetime.Token.ThrowIfCancellationRequested();
            if (depth > 16) throw new ChatApiException("Les dossiers sont limités à 16 niveaux.");
            var path = prefix + item.Name;
            if (!includeExcluded && ImportPolicy.Excluded(path)) { excluded++; return; }
            var local = item.TryGetLocalPath();
            if (local is not null && (File.GetAttributes(local) & FileAttributes.ReparsePoint) != 0)
                throw new ChatApiException("Les liens symboliques ne sont pas importés. Sélectionnez leur cible explicitement.");
            if (item is IStorageFolder folder)
            {
                if (!seenFolders.Add(folder.Path.ToString())) return;
                await foreach (var child in folder.GetItemsAsync()) await VisitAsync(child, path + "/", depth + 1);
            }
            else if (item is IStorageFile file)
            {
                if (files.Count >= 500) throw new ChatApiException("Import limité à 500 fichiers par lot.");
                var properties = await file.GetBasicPropertiesAsync();
                if (properties.Size > 25 * 1024 * 1024) throw new ChatApiException($"{file.Name} dépasse la limite de 25 Mio.");
                totalBytes += properties.Size ?? 0;
                if (totalBytes > 100 * 1024 * 1024) throw new ChatApiException("Lot limité à 100 Mio.");
                files.Add(new(path, await file.OpenReadAsync(), properties.DateCreated, properties.DateModified));
            }
        }
        try
        {
            foreach (var item in selected) await VisitAsync(item, "", 0);
            if (files.Count == 0) { Feedback("Aucun fichier sélectionné (les dossiers vides ne sont pas importés)."); return; }
            Feedback($"Import de {files.Count} fichier(s)…");
            var result = await _client.UploadAsync(_current.Id, Guid.NewGuid(), files, _lifetime.Token);
            await LoadAsync(_current);
            Feedback($"{result.Length} fichier(s) enregistrés · {excluded} élément(s) écarté(s) avant import · "
                + $"{result.Count(file => file.ContextMode == "excluded")} contenu(s) exclu(s) de l’IA. "
                + "Ouvrez Synthèses de dossiers pour produire tree.txt et une synthèse récursive.");
        }
        finally
        {
            foreach (var file in files) await file.Content.DisposeAsync();
        }
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
