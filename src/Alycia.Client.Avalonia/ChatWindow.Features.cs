using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Alycia.Contracts.Chat;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;

namespace Alycia.Client.Avalonia;

public partial class ChatWindow
{
    private void InitializeFeatureControls()
    {
        C<Button>("AnalysisButton").Click += async (_, _) => await RunAsync(AnalysesAsync);
        C<Button>("ProjectToolsButton").Click += async (_, _) => await RunAsync(ProjectToolsAsync);
        C<Button>("ExportConfigurationButton").Click += async (_, _) => await RunAsync(ExportConfigurationAsync);
        C<Button>("ImportConfigurationButton").Click += async (_, _) => await RunAsync(ImportConfigurationAsync);
        C<Button>("SettingsButton").Click += async (_, _) => await RunAsync(SettingsAsync);
        C<Button>("ActiveModelButton").Click += async (_, _) => await RunAsync(SettingsAsync);
        C<Button>("ContextButton").Click += async (_, _) => await RunAsync(() => OpenInspectorAsync("context"));
        C<Button>("FilesButton").Click += async (_, _) => await RunAsync(ManageFilesAsync);
        C<Button>("ForkButton").Click += async (_, _) => await RunAsync(() => ForkAsync(false));
        C<Button>("RegenerateButton").Click += async (_, _) => await RunAsync(() => ForkAsync(true));
        C<Button>("ExportButton").Click += async (_, _) => await RunAsync(ExportAsync);
        C<Button>("ImportConversationButton").Click += async (_, _) => await RunAsync(ImportConversationAsync);
        C<Button>("PromptsButton").Click += async (_, _) => await RunAsync(PromptsAsync);
        C<Button>("SearchButton").Click += async (_, _) => await RunAsync(async () =>
        {
            await RefreshConversationsAsync(null);
            if (_current is not null) await LoadAsync(_current);
        });
        C<Button>("MoreConversationsButton").Click += async (_, _) => await RunAsync(async () =>
        {
            var list = C<ListBox>("ConversationList");
            var items = list.ItemsSource?.Cast<ConversationSummary>().ToArray() ?? [];
            var page = await _client.SearchAsync(_searchQuery, items.Length, _lifetime.Token, _project.Id, true);
            list.ItemsSource = items.Concat(page.Items).DistinctBy(c => c.Id).ToArray();
            list.SelectedItem = _current;
            C<Button>("MoreConversationsButton").IsVisible = page.HasMore;
            Feedback("Conversations suivantes chargées.");
        });
    }

    private async Task<bool> EditDialogAsync(string title, Control content, string action = "Enregistrer", Func<string?>? validate = null)
    {
        var dialog = new Window
        {
            Title = title,
            Width = Math.Clamp(Bounds.Width - 48, 360, 700),
            Height = Math.Clamp(Bounds.Height - 48, 320, 680),
            MinWidth = 360,
            MinHeight = 320,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        var cancel = new Button { Content = "Fermer" };
        var save = new Button { Content = action };
        cancel.Click += (_, _) => dialog.Close(false);
        var error = new TextBlock { Foreground = Appearance.Color(this, "Error"), TextWrapping = TextWrapping.Wrap, IsVisible = false };
        save.Click += (_, _) =>
        {
            var message = validate?.Invoke();
            error.Text = message; error.IsVisible = message is not null;
            if (message is null) dialog.Close(true);
        };
        dialog.KeyDown += (_, e) => { if (e.Key == global::Avalonia.Input.Key.Escape) dialog.Close(false); };
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 12,
            Margin = new Thickness(0, 16, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Right,
            Children = { cancel, save }
        };
        var grid = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto"), Margin = new Thickness(24), RowSpacing = 10 };
        grid.Children.Add(error);
        var scroll = new ScrollViewer { Content = content, HorizontalScrollBarVisibility = global::Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        Grid.SetRow(scroll, 1); grid.Children.Add(scroll);
        Grid.SetRow(buttons, 2); grid.Children.Add(buttons); dialog.Content = grid;
        return await dialog.ShowDialog<bool>(this);
    }

    private async Task SettingsAsync()
    {
        if (_current is null) return;
        var updated = await new ConversationSettingsEditor(_client).EditAsync(_current.Id,
            (panel, validate) => EditDialogAsync("Réglages de la conversation", panel, validate: validate), _lifetime.Token);
        if (updated is null) return;
        _current = updated;
        await RefreshConversationsAsync(_current.Id);
        await LoadAsync(_current!);
        Feedback("Réglages enregistrés pour les prochains échanges.");
    }

    private Task ManageFilesAsync() => ShowDocumentsAsync(_project.Id.HasValue);

    private async Task ForkAsync(bool regenerate)
    {
        if (_current is null) return;
        var original = _current;
        string? question = null;
        if (regenerate)
        {
            var page = await _client.MessagesAsync(original.Id, null, _lifetime.Token);
            original = page.Conversation;
            question = page.Messages.LastOrDefault(m => m.Role == "user")?.Content;
            if (question is null) return;
        }
        var copy = await _client.ForkAsync(original.Id, original.Revision,
            regenerate ? original.MessageCount - 2 : null, _lifetime.Token);
        C<TextBox>("SearchInput").Text = "";
        await RefreshConversationsAsync(copy.Id);
        await LoadAsync(copy);
        if (question is not null)
        {
            C<TextBox>("MessageInput").Text = question;
            await SendAsync();
        }
        else Feedback("Conversation et documents copiés. L’original est conservé.");
    }

    private async Task ExportAsync()
    {
        if (_current is null) return;
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Exporter l’historique et les réglages (sans les fichiers joints)",
            SuggestedFileName = "alycia-conversation.json",
            FileTypeChoices = [new("JSON réimportable") { Patterns = ["*.json"] }, new("Markdown") { Patterns = ["*.md"] }]
        });
        if (file is null) return;
        var bytes = await _client.ExportAsync(_current.Id, file.Name.EndsWith(".md", StringComparison.OrdinalIgnoreCase)
            ? "markdown" : "json", _lifetime.Token);
        await using var output = await file.OpenWriteAsync();
        if (output.CanSeek) output.SetLength(0);
        await output.WriteAsync(bytes, _lifetime.Token);
        Feedback("Historique exporté. Les fichiers joints nécessitent la sauvegarde complète.");
    }

    private async Task ImportConversationAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        { Title = "Importer une conversation Alycia JSON", AllowMultiple = false, FileTypeFilter = [new("JSON") { Patterns = ["*.json"] }] });
        if (files.Count == 0) return;
        await using var input = await files[0].OpenReadAsync();
        using var buffer = new MemoryStream();
        var bytes = new byte[65536];
        int count;
        while ((count = await input.ReadAsync(bytes, _lifetime.Token)) > 0)
        {
            if (buffer.Length + count > ChatArchiveLimits.MaxBytes) throw new ChatApiException("Import limité à 5 Mio.");
            buffer.Write(bytes, 0, count);
        }
        var conversation = await _client.ImportConversationAsync(buffer.ToArray(), _lifetime.Token);
        SaveDraft();
        await RefreshProjectsAsync(conversation.WorkspaceId);
        C<TextBox>("SearchInput").Text = "";
        await RefreshConversationsAsync(conversation.Id);
        await LoadAsync(conversation);
        Feedback("Conversation importée dans un nouvel historique, sans fichiers joints.");
    }

    private async void CopyMessageClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { CommandParameter: ChatMessageItem item })
            await CopyAsync(item.Content);
    }

    private async void CopyCodeClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { CommandParameter: ChatMessageItem item }) return;
        var code = CodeBlocks.Extract(item.Content);
        if (code.Length == 0) { Feedback("Aucun bloc de code délimité dans ce message."); return; }
        await CopyAsync(code);
    }

    private async Task CopyAsync(string text)
    {
        try
        {
            if (Clipboard is null) { Feedback("Presse-papiers indisponible."); return; }
            await Clipboard.SetTextAsync(text);
            Feedback("Copié dans le presse-papiers.");
        }
        catch (Exception) { Feedback("Le presse-papiers est indisponible."); }
    }
}

public static class CodeBlocks
{
    public static string Extract(string markdown)
    {
        using var reader = new StringReader(markdown);
        var result = new StringBuilder();
        string? fence = null;
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            var trimmed = line.TrimStart();
            if (fence is null)
            {
                if (trimmed.StartsWith("```", StringComparison.Ordinal) || trimmed.StartsWith("~~~", StringComparison.Ordinal))
                    fence = new string(trimmed[0], trimmed.TakeWhile(c => c == trimmed[0]).Count());
            }
            else if (trimmed.StartsWith(fence, StringComparison.Ordinal) && trimmed.Trim(fence[0], ' ', '\t').Length == 0)
            { fence = null; result.AppendLine(); }
            else result.AppendLine(line);
        }
        return result.ToString().TrimEnd();
    }
}
