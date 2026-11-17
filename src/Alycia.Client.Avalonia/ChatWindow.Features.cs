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
        C<Button>("AnalysisButton").Click += async (_, _) => await RunAsync(AnalysesAsync);
        C<Button>("ProjectToolsButton").Click += async (_, _) => await RunAsync(ProjectToolsAsync);
        C<Button>("SettingsButton").Click += async (_, _) => await RunAsync(SettingsAsync);
        C<Button>("ActiveModelButton").Click += async (_, _) => await RunAsync(SettingsAsync);
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
        Text = text,
        MaxLength = length,
        AcceptsReturn = multiline,
        TextWrapping = TextWrapping.Wrap,
        MinHeight = multiline ? 90 : 32,
        MaxHeight = multiline ? 180 : 50
    };

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
        var error = new TextBlock { Foreground = Brush.Parse("#F2A9B5"), TextWrapping = TextWrapping.Wrap, IsVisible = false };
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
        var settings = await _client.SettingsAsync(_current.Id, _lifetime.Token);
        var current = settings.Conversation;
        var models = await _client.ModelsAsync(_lifetime.Token);
        var model = new ComboBox
        {
            Name = "ConversationModelSelector",
            ItemsSource = models,
            SelectedItem = models.Contains(current.Model, StringComparer.Ordinal) ? current.Model : null,
            PlaceholderText = "Choisir un modèle génératif local",
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        var system = Input(current.SystemPrompt, 4000, true);
        var notes = Input(current.ContextNotes, 8000, true);
        var options = settings.Options;
        var context = Input(options.ContextTokens.ToString(), 6);
        var output = Input(options.MaxOutputTokens.ToString(), 6);
        var turns = Input(options.MaxHistoryTurns.ToString(), 3);
        var temperature = Input(options.Temperature.ToString(CultureInfo.InvariantCulture), 10);
        var seed = Input(options.Seed?.ToString() ?? "", 12);
        var timeout = Input(options.TimeoutSeconds.ToString(), 3);
        var calls = Input(options.SummaryMaxCalls.ToString(), 4);
        var toolCalls = Input(options.ToolMaxCalls.ToString(), 3);
        var retrieval = new ComboBox { ItemsSource = new[] { "legacy", "lexical", "hybrid" }, SelectedItem = options.RetrievalMode };
        var embedding = Input(options.EmbeddingModel, 200);
        var tokenizer = new CheckBox { Content = "Utiliser le tokenizer compatible du modèle quand disponible", IsChecked = options.TokenCounting == "auto" };
        var tools = new CheckBox { Content = "Autoriser les outils de lecture des sources importées", IsChecked = options.UseProjectTools };
        var continuous = new CheckBox { Content = "Générer jusqu’au signal d’arrêt du modèle", IsChecked = options.GenerateUntilStop };
        var structured = new CheckBox { Content = "Mémoire structurée : citations, décisions et contraintes", IsChecked = options.StructuredMemory };
        var recursive = new CheckBox { Content = "Résumer les anciens échanges et préserver les deux derniers", IsChecked = options.AutoSummarize };
        turns.IsEnabled = !options.AutoSummarize;
        recursive.IsCheckedChanged += (_, _) => turns.IsEnabled = recursive.IsChecked != true;
        var panel = new StackPanel { Spacing = 10 };
        var presets = new ComboBox
        {
            ItemsSource = BuiltInPrompts.Concat(await _client.PromptsAsync(_lifetime.Token)).ToArray(),
            PlaceholderText = "Charger un profil de prompt"
        };
        presets.SelectionChanged += (_, _) => { if (presets.SelectedItem is PromptItem item) system.Text = item.SystemPrompt; };
        panel.Children.Add(Label($"Modèle actuellement enregistré : {current.Model}"));
        if (!models.Contains(current.Model, StringComparer.Ordinal))
            panel.Children.Add(Label("Ce modèle n’est pas disponible pour la génération locale. Sélectionnez un modèle ci-dessous pour reprendre cette conversation ; votre brouillon est conservé."));
        panel.Children.Add(continuous); panel.Children.Add(recursive); panel.Children.Add(tools); panel.Children.Add(tokenizer); panel.Children.Add(presets);
        foreach (var (label, control) in new (string, Control)[] {
            ("Modèle génératif pour cette conversation", model),
            ("Mémoire des échanges anciens", structured),
            ("Recherche : lexical = BM25 ; hybrid = BM25 + embeddings locaux ; legacy = V3", retrieval),
            ("Modèle d’embeddings installé (hybrid uniquement, nom exact)", embedding), ("Instruction système", system),
            ("Notes persistantes du projet — toujours incluses dans le budget", notes),
            ("Fenêtre du modèle (tokens, 1024 à 131072)", context), ("Réserve de contexte pour la sortie (limite de génération seulement si le mode continu est désactivé)", output),
            ("Fenêtre simple : échanges récents (uniquement sans résumé automatique)", turns), ("Température (0 à 2, virgule ou point)", temperature),
            ("Graine facultative (ne garantit pas un résultat identique sur tous les matériels)", seed),
            ("Inactivité maximale pendant une génération (secondes, 1 à 600)", timeout),
            ("Nombre maximal d’appels par opération de résumé (8 à 2048)", calls),
            ("Appels d’outils par demande (1 à 128, modèle compatible requis)", toolCalls) })
        { panel.Children.Add(Label(label)); panel.Children.Add(control); }
        panel.Children.Add(Label("Les modifications s’appliquent aux prochains échanges. L’historique reste intact. Une grande fenêtre augmente l’usage de mémoire."));
        ChatOptions? edited = null;
        string? Validate()
        {
            if (model.SelectedItem is not string modelName || !models.Contains(modelName, StringComparer.Ordinal))
                return "Choisissez un modèle génératif local disponible.";
            if (!int.TryParse(context.Text, out var contextValue) || !int.TryParse(output.Text, out var outputValue)
                || !int.TryParse(turns.Text, out var turnsValue) || !int.TryParse(timeout.Text, out var timeoutValue)
                || !int.TryParse(calls.Text, out var callsValue) || !int.TryParse(toolCalls.Text, out var toolCallsValue)
                || !double.TryParse(temperature.Text?.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var temperatureValue)
                || !string.IsNullOrWhiteSpace(seed.Text) && !int.TryParse(seed.Text, out _))
                return "Un réglage numérique est invalide. Les valeurs saisies sont conservées.";
            edited = new ChatOptions
            {
                RetrievalMode = retrieval.SelectedItem as string ?? "lexical",
                EmbeddingModel = embedding.Text?.Trim() ?? "",
                TokenCounting = tokenizer.IsChecked == true ? "auto" : "conservative",
                ContextTokens = contextValue,
                MaxOutputTokens = outputValue,
                MaxHistoryTurns = turnsValue,
                TimeoutSeconds = timeoutValue,
                Temperature = temperatureValue,
                SafetyTokens = options.SafetyTokens,
                SummaryMaxCalls = callsValue,
                ToolMaxCalls = toolCallsValue,
                UseProjectTools = tools.IsChecked == true,
                GenerateUntilStop = continuous.IsChecked == true,
                AutoSummarize = recursive.IsChecked == true,
                StructuredMemory = structured.IsChecked == true,
                Seed = string.IsNullOrWhiteSpace(seed.Text) ? null : int.Parse(seed.Text!)
            };
            try
            {
                edited.Validate();
                var sample = Alycia.Modules.Chat.Domain.Conversation.Create("test", modelName, system.Text);
                sample.Configure(modelName, system.Text, notes.Text, "{}");
                SlidingContext.Build(ConversationConfiguration.System(sample), [], "Bonjour", edited);
                return null;
            }
            catch (ArgumentException exception) { return exception.Message; }
        }
        if (!await EditDialogAsync("Réglages de la conversation", panel, validate: Validate)) return;
        _current = await _client.ConfigureAsync(current.Id, current.Revision, (string)model.SelectedItem!, system.Text ?? "",
            notes.Text ?? "", edited!, _lifetime.Token);
        await RefreshConversationsAsync(_current.Id);
        await LoadAsync(_current!);
        Feedback("Réglages enregistrés pour les prochains échanges.");
    }

    private Task PreviewAsync() => MemoryAsync();

    private async Task ManageFilesAsync()
    {
        if (_current is null) return;
        var settings = await _client.SettingsAsync(_current.Id, _lifetime.Token);
        var files = await _client.FilesAsync(_current.Id, _lifetime.Token);
        var panel = new StackPanel { Spacing = 12 };
        panel.Children.Add(Label("Fenêtre : extraits liés aux échanges récents. Projet : extraits persistants. Épinglé : priorité aux extraits. Exclu : contenu inaccessible aux outils et au modèle. Les outils peuvent consulter tous les imports non exclus. Les extraits initiaux restent sélectifs."));
        var controls = new Dictionary<Guid, ComboBox>();
        foreach (var file in files)
        {
            panel.Children.Add(Label(file.Name + " · " + file.Status));
            var mode = new ComboBox
            {
                ItemsSource = new[] { "Fenêtre", "Épinglé", "Exclu", "Projet (persistant)" },
                SelectedIndex = file.ContextMode == "pinned" ? 1 : file.ContextMode == "excluded" ? 2 : file.ContextMode == "project" ? 3 : 0
            };
            controls.Add(file.Id, mode); panel.Children.Add(mode);
        }
        if (!await EditDialogAsync("Documents de la conversation", panel)) return;
        var revision = settings.Conversation.Revision;
        foreach (var file in files)
        {
            var mode = controls[file.Id].SelectedIndex switch { 1 => "pinned", 2 => "excluded", 3 => "project", _ => "window" };
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
