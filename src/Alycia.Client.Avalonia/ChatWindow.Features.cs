using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Alycia.Modules.Chat.Application;
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
        C<Button>("SettingsButton").Click += async (_, _) => await RunAsync(SettingsAsync);
        C<Button>("ContextButton").Click += async (_, _) => await RunAsync(PreviewAsync);
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
            var page = await _client.SearchAsync(_searchQuery, items.Length, _lifetime.Token);
            list.ItemsSource = items.Concat(page.Items).DistinctBy(c => c.Id).ToArray();
            list.SelectedItem = _current;
            C<Button>("MoreConversationsButton").IsVisible = page.HasMore;
            Feedback("Conversations suivantes chargées.");
        });
    }

    private static TextBlock Label(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap };
    private static TextBox Input(string text, int length, bool multiline = false) => new()
    {
        Text = text, MaxLength = length, AcceptsReturn = multiline, TextWrapping = TextWrapping.Wrap,
        MinHeight = multiline ? 90 : 32, MaxHeight = multiline ? 180 : 50
    };

    private async Task<bool> EditDialogAsync(string title, Control content, string action = "Enregistrer")
    {
        var dialog = new Window { Title = title, Width = 700, Height = 680, MinWidth = 500, MinHeight = 400,
            WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var cancel = new Button { Content = "Fermer" };
        var save = new Button { Content = action };
        cancel.Click += (_, _) => dialog.Close(false);
        save.Click += (_, _) => dialog.Close(true);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Margin = new Thickness(0, 16, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Right, Children = { cancel, save } };
        var grid = new Grid { RowDefinitions = new RowDefinitions("*,Auto"), Margin = new Thickness(24) };
        grid.Children.Add(new ScrollViewer { Content = content, HorizontalScrollBarVisibility = global::Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled });
        Grid.SetRow(buttons, 1); grid.Children.Add(buttons); dialog.Content = grid;
        return await dialog.ShowDialog<bool>(this);
    }

    private async Task SettingsAsync()
    {
        if (_current is null) return;
        var settings = await _client.SettingsAsync(_current.Id, _lifetime.Token);
        var current = settings.Conversation;
        var model = Input(current.Model, 200);
        var system = Input(current.SystemPrompt, 4000, true);
        var notes = Input(current.ContextNotes, 8000, true);
        var options = settings.Options;
        var context = Input(options.ContextTokens.ToString(), 6);
        var output = Input(options.MaxOutputTokens.ToString(), 6);
        var turns = Input(options.MaxHistoryTurns.ToString(), 3);
        var temperature = Input(options.Temperature.ToString(CultureInfo.InvariantCulture), 10);
        var seed = Input(options.Seed?.ToString() ?? "", 12);
        var timeout = Input(options.TimeoutSeconds.ToString(), 3);
        var panel = new StackPanel { Spacing = 10 };
        var presets = new ComboBox { ItemsSource = BuiltInPrompts.Concat(await _client.PromptsAsync(_lifetime.Token)).ToArray(),
            PlaceholderText = "Charger un profil de prompt" };
        presets.SelectionChanged += (_, _) => { if (presets.SelectedItem is PromptItem item) system.Text = item.SystemPrompt; };
        panel.Children.Add(presets);
        foreach (var (label, control) in new (string, Control)[] {
            ("Modèle local (nom exact dans Ollama)", model), ("Instruction système", system),
            ("Notes persistantes du projet — toujours incluses dans le budget", notes),
            ("Fenêtre du modèle (tokens, 1024 à 131072)", context), ("Réserve de sortie (tokens)", output),
            ("Échanges récents maximum (0 à 100)", turns), ("Température (0 à 2, point décimal)", temperature),
            ("Graine facultative (ne garantit pas un résultat identique sur tous les matériels)", seed),
            ("Délai de génération (secondes, 1 à 600)", timeout) })
        { panel.Children.Add(Label(label)); panel.Children.Add(control); }
        panel.Children.Add(Label("Les modifications s’appliquent aux prochains échanges. L’historique reste intact. Une grande fenêtre augmente l’usage de mémoire."));
        if (!await EditDialogAsync("Réglages de la conversation", panel)) return;
        if (!int.TryParse(context.Text, out var contextValue) || !int.TryParse(output.Text, out var outputValue)
            || !int.TryParse(turns.Text, out var turnsValue) || !int.TryParse(timeout.Text, out var timeoutValue)
            || !double.TryParse(temperature.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var temperatureValue)
            || !string.IsNullOrWhiteSpace(seed.Text) && !int.TryParse(seed.Text, out _))
            throw new ChatApiException("Un réglage numérique est invalide.");
        var edited = new ChatOptions { ContextTokens = contextValue, MaxOutputTokens = outputValue,
            MaxHistoryTurns = turnsValue, TimeoutSeconds = timeoutValue, Temperature = temperatureValue,
            SafetyTokens = options.SafetyTokens, Seed = string.IsNullOrWhiteSpace(seed.Text) ? null : int.Parse(seed.Text!) };
        _current = await _client.ConfigureAsync(current.Id, current.Revision, model.Text ?? "", system.Text ?? "",
            notes.Text ?? "", edited, _lifetime.Token);
        await RefreshConversationsAsync(_current.Id);
        await LoadAsync(_current!);
        Feedback("Réglages enregistrés pour les prochains échanges.");
    }

    private async Task PreviewAsync()
    {
        if (_current is null) return;
        var text = C<TextBox>("MessageInput").Text?.Trim();
        if (string.IsNullOrEmpty(text)) { Feedback("Saisissez un message pour inspecter son contexte."); return; }
        var preview = await _client.PreviewAsync(_current.Id, text, _lifetime.Token);
        var panel = new StackPanel { Spacing = 14 };
        panel.Children.Add(Label($"Entrée estimée : {preview.Context.EstimatedInputTokens} / "
            + $"{preview.Options.ContextTokens - preview.Options.MaxOutputTokens - preview.Options.SafetyTokens} ; "
            + $"{preview.Context.HistoryMessagesIncluded} messages inclus, {preview.OmittedMessages} omis ; "
            + $"{preview.Context.FilesIncluded} documents.\n{preview.Estimator}. Les messages omis restent dans l’historique."));
        panel.Children.Add(new SelectableTextBlock { Text = string.Join("\n\n", preview.Context.Messages
            .Select(m => $"[{m.Role}]\n{m.Content}")), TextWrapping = TextWrapping.Wrap });
        await EditDialogAsync("Contexte effectivement préparé", panel, "Fermer");
    }

    private async Task ManageFilesAsync()
    {
        if (_current is null) return;
        var settings = await _client.SettingsAsync(_current.Id, _lifetime.Token);
        var files = await _client.FilesAsync(_current.Id, _lifetime.Token);
        var panel = new StackPanel { Spacing = 12 };
        panel.Children.Add(Label("Fenêtre : suit les échanges récents. Épinglé : reste candidat au contexte. Exclu : conservé sans envoi au modèle. Le budget peut tronquer ou omettre un document, même épinglé."));
        var controls = new Dictionary<Guid, ComboBox>();
        foreach (var file in files)
        {
            panel.Children.Add(Label(file.Name + " · " + file.Status));
            var mode = new ComboBox { ItemsSource = new[] { "Fenêtre", "Épinglé", "Exclu" },
                SelectedIndex = file.ContextMode == "pinned" ? 1 : file.ContextMode == "excluded" ? 2 : 0 };
            controls.Add(file.Id, mode); panel.Children.Add(mode);
        }
        if (!await EditDialogAsync("Documents de la conversation", panel)) return;
        var revision = settings.Conversation.Revision;
        foreach (var file in files)
        {
            var mode = controls[file.Id].SelectedIndex switch { 1 => "pinned", 2 => "excluded", _ => "window" };
            if (mode == file.ContextMode) continue;
            await _client.ConfigureFileAsync(_current.Id, file.Id, revision, mode, _lifetime.Token);
            revision++;
        }
        await LoadAsync(_current);
        Feedback("Sélection documentaire enregistrée.");
    }

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
            if (buffer.Length + count > ConversationTransfer.MaxBytes) throw new ChatApiException("Import limité à 5 Mio.");
            buffer.Write(bytes, 0, count);
        }
        var conversation = await _client.ImportConversationAsync(buffer.ToArray(), _lifetime.Token);
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
