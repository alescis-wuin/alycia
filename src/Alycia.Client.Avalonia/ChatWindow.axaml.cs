using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Threading;

namespace Alycia.Client.Avalonia;

public partial class ChatWindow : Window
{
    private readonly ChatApiClient _client;
    private readonly DraftStore _drafts;
    private readonly DispatcherTimer _draftTimer = new() { Interval = TimeSpan.FromMilliseconds(750) };
    private bool _loadingDraft;
    private string _searchQuery = "";
    private readonly CancellationTokenSource _lifetime = new();
    private readonly ObservableCollection<ChatMessageItem> _messages = [];
    private CancellationTokenSource? _generation;
    private ConversationSummary? _current;
    private bool _busy;
    private Guid _pendingId;
    private bool _followOutput = true;
    private bool _sidebarManual;

    public ChatWindow() : this(new ChatApiClient()) { }

    internal ChatWindow(ChatApiClient client, DraftStore? drafts = null)
    {
        _client = client;
        _drafts = drafts ?? new DraftStore();
        InitializeComponent();
        C<ItemsControl>("MessageList").ItemsSource = _messages;
        Opened += async (_, _) => await RunAsync(InitializeAsync);
        Closed += (_, _) => { _draftTimer.Stop(); SaveDraft(); _drafts.Save(); _lifetime.Cancel(); };
        _draftTimer.Tick += (_, _) => { _draftTimer.Stop(); _drafts.Save(); };
        C<TextBox>("MessageInput").TextChanged += (_, _) =>
        {
            if (!_loadingDraft) { SaveDraft(); _draftTimer.Stop(); _draftTimer.Start(); }
            UpdateControls();
        };
        C<Button>("RefreshButton").Click += async (_, _) => await RunAsync(InitializeAsync);
        C<Button>("NewButton").Click += async (_, _) => await RunAsync(CreateAsync);
        C<Button>("RenameButton").Click += async (_, _) => await RunAsync(RenameAsync);
        C<Button>("DeleteButton").Click += async (_, _) => await RunAsync(DeleteAsync);
        C<Button>("SendButton").Click += async (_, _) => await RunAsync(SendAsync);
        C<Button>("CancelButton").Click += (_, _) => _generation?.Cancel();
        C<Button>("OlderButton").Click += async (_, _) => await RunAsync(LoadOlderAsync);
        C<Button>("DiagnosticsButton").Click += (_, _) => new MainWindow().Show(this);
        C<ListBox>("ConversationList").SelectionChanged += async (_, _) =>
        {
            if (!_busy && C<ListBox>("ConversationList").SelectedItem is ConversationSummary selected)
            {
                await RunAsync(() => LoadAsync(selected));
            }
        };
        C<TextBox>("MessageInput").KeyDown += async (_, e) =>
        {
            if (e.Key == Key.Enter && e.KeyModifiers.HasFlag(KeyModifiers.Control))
            {
                e.Handled = true;
                await RunAsync(SendAsync);
            }
        };
        InitializeFileControls();
        InitializeFeatureControls();
        C<MarkdownView>("StreamingText").Rendered += ScrollToEnd;
        C<ScrollViewer>("MessageScroll").ScrollChanged += (_, e) =>
        {
            if (e.OffsetDelta.Y != 0 && e.ExtentDelta.Y == 0)
            {
                var scroll = C<ScrollViewer>("MessageScroll");
                _followOutput = scroll.Extent.Height - scroll.Viewport.Height - scroll.Offset.Y < 70;
                C<Button>("LatestButton").IsVisible = !_followOutput;
            }
        };
        C<Button>("LatestButton").Click += (_, _) => { _followOutput = true; C<Button>("LatestButton").IsVisible = false; ScrollToEnd(); };
        C<Button>("SidebarToggle").Click += (_, _) => { _sidebarManual = true; C<Border>("Sidebar").IsVisible = !C<Border>("Sidebar").IsVisible; };
        SizeChanged += (_, _) => { if (!_sidebarManual) C<Border>("Sidebar").IsVisible = Bounds.Width >= 1040; };
        C<TextBox>("SearchInput").KeyDown += async (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true; await RunAsync(async () =>
            { await RefreshConversationsAsync(null); if (_current is not null) await LoadAsync(_current); });
            }
        };
        KeyDown += async (_, e) =>
        {
            if (e.Key == Key.Escape && _generation is not null) { _generation.Cancel(); e.Handled = true; }
            if (!e.KeyModifiers.HasFlag(KeyModifiers.Control)) return;
            if (e.Key == Key.N) { e.Handled = true; await RunAsync(CreateAsync); }
            if (e.Key == Key.F) { e.Handled = true; _sidebarManual = true; C<Border>("Sidebar").IsVisible = true; C<TextBox>("SearchInput").Focus(); }
        };
        UpdateControls();
    }

    private T C<T>(string name) where T : Control => this.FindControl<T>(name)
        ?? throw new InvalidOperationException($"Control {name} not found.");

    private void Feedback(string message, bool error = false)
    {
        C<TextBlock>("FeedbackText").Text = message;
        C<TextBlock>("FeedbackText").Foreground = global::Avalonia.Media.Brush.Parse(error ? "#F0A9B5" : "#AAB8D0");
    }

    private async Task RunAsync(Func<Task> action)
    {
        if (_busy || _lifetime.IsCancellationRequested)
        {
            return;
        }

        _busy = true;
        UpdateControls();
        Feedback("Chargement…");
        try
        {
            await action();
        }
        catch (OperationCanceledException)
        {
            Feedback("Opération interrompue. Brouillon et données originales conservés ; rechargez pour vérifier l’état final.", true);
        }
        catch (ChatApiException exception)
        {
            Feedback(exception.Message, true);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Chat operation failed: {exception.GetType().Name}");
            Feedback("Une erreur inattendue est survenue. Votre texte est conservé.", true);
        }
        finally
        {
            _busy = false;
            UpdateControls();
        }
    }

    private void UpdateControls()
    {
        C<StackPanel>("EmptyState").IsVisible = _messages.Count == 0 && _generation is null
            && !C<Expander>("StreamingPanel").IsVisible && !C<Border>("PendingUserPanel").IsVisible;
        C<Button>("ActiveModelButton").IsVisible = _current is not null;
        C<Button>("ActiveModelButton").IsEnabled = !_busy && _current is not null;
        C<TextBlock>("EmptyHint").Text = _current is null
            ? "Choisissez un modèle et créez votre première conversation."
            : "Posez votre première question.";
        C<StackPanel>("CreationPanel").IsEnabled = !_busy;
        C<WrapPanel>("ConversationTools").IsEnabled = !_busy && _current is not null;
        C<Button>("RegenerateButton").IsEnabled = !_busy && _current is { MessageCount: >= 2 };
        foreach (var name in new[] { "SearchButton", "MoreConversationsButton", "PromptsButton", "ImportConversationButton" })
            C<Button>(name).IsEnabled = !_busy;
        C<ListBox>("ConversationList").IsEnabled = !_busy;
        C<Button>("RefreshButton").IsEnabled = !_busy;
        C<Grid>("ConversationHeader").IsEnabled = !_busy && _current is not null;
        C<Button>("UploadButton").IsEnabled = !_busy && _current is not null;
        C<Button>("FolderButton").IsEnabled = !_busy && _current is not null;
        C<Button>("SendButton").IsEnabled = !_busy && _current is not null && !string.IsNullOrWhiteSpace(C<TextBox>("MessageInput").Text);
        C<TextBox>("MessageInput").IsEnabled = !_busy && _current is not null;
        C<Button>("OlderButton").IsEnabled = !_busy;
        C<ProgressBar>("BusyIndicator").IsVisible = _busy;
        C<Button>("CancelButton").IsVisible = _generation is not null;
    }

    private async Task InitializeAsync()
    {
        // Load persisted history even when Ollama is temporarily unavailable.
        await RefreshConversationsAsync(_current?.Id);
        if (_current is not null)
        {
            await LoadAsync(_current);
        }

        var models = await _client.ModelsAsync(_lifetime.Token);
        var selector = C<ComboBox>("ModelSelector");
        var selected = selector.SelectedItem as string;
        selector.ItemsSource = models;
        selector.SelectedItem = models.Contains(selected) ? selected : models.FirstOrDefault();
        Feedback(models.Length == 0
            ? "Aucun modèle génératif local. Les modèles d’embeddings ne peuvent pas discuter. Installez un modèle génératif avec ollama pull, puis actualisez."
            : "Prêt à discuter." + (_drafts.Error is null ? "" : " " + _drafts.Error));
    }

    private async Task RefreshConversationsAsync(Guid? selectId)
    {
        SaveDraft();
        _searchQuery = C<TextBox>("SearchInput").Text ?? "";
        var page = await _client.SearchAsync(_searchQuery, 0, _lifetime.Token);
        var conversations = page.Items;
        C<Button>("MoreConversationsButton").IsVisible = page.HasMore;
        var list = C<ListBox>("ConversationList");
        list.ItemsSource = conversations;
        var selected = conversations.FirstOrDefault(item => item.Id == selectId) ?? conversations.FirstOrDefault();
        if (selectId.HasValue && selected?.Id != selectId.Value)
        {
            // Keep a newly created or renamed conversation visible even if a search filter excludes it.
            selected = (await _client.SettingsAsync(selectId.Value, _lifetime.Token)).Conversation;
            list.ItemsSource = new[] { selected }.Concat(conversations).ToArray();
        }
        _current = selected;
        _loadingDraft = true;
        C<TextBox>("MessageInput").Text = _current is null ? "" : _drafts.Get(_current.Id)?.Text ?? "";
        _loadingDraft = false;
        list.SelectedItem = _current;
        if (_current is null)
        {
            _messages.Clear();
            C<Expander>("StreamingPanel").IsVisible = false;
            C<Border>("PendingUserPanel").IsVisible = false;
            C<MarkdownView>("StreamingText").Text = "";
            C<Expander>("StreamingTools").IsVisible = false;
            C<SelectableTextBlock>("StreamingToolText").Text = "";
            C<SelectableTextBlock>("PendingUserText").Text = "";
            C<TextBox>("TitleInput").Text = "";
            C<TextBlock>("ConversationDetails").Text = "Créez une conversation pour commencer.";
            C<Button>("OlderButton").IsVisible = false;
        }
    }

    private async Task LoadAsync(ConversationSummary conversation)
    {
        SaveDraft();
        var page = await _client.MessagesAsync(conversation.Id, null, _lifetime.Token);
        _current = page.Conversation;
        _loadingDraft = true;
        C<TextBox>("MessageInput").Text = _drafts.Get(_current.Id)?.Text ?? "";
        _loadingDraft = false;
        C<Expander>("StreamingPanel").IsVisible = false;
        C<Border>("PendingUserPanel").IsVisible = false;
        _followOutput = true; C<Button>("LatestButton").IsVisible = false;
        C<TextBox>("TitleInput").Text = _current.Title;
        C<Button>("ActiveModelButton").Content = $"Modèle actif : {_current.Model} ▾";
        C<TextBlock>("ConversationDetails").Text = $"{_current.MessageCount} messages"
            + (_current.MemoryThroughSequence > 0 ? $" · mémoire : messages 1–{_current.MemoryThroughSequence}" : " · contexte local");
        _messages.Clear();
        foreach (var message in Timeline(page))
        {
            _messages.Add(message);
        }

        C<Button>("OlderButton").IsVisible = page.HasMore;
        ScrollToEnd();
        Feedback("Historique chargé.");
    }

    private async Task LoadOlderAsync()
    {
        if (_current is null || _messages.Count == 0)
        {
            return;
        }

        var page = await _client.MessagesAsync(_current.Id, _messages[0].Sequence, _lifetime.Token);
        var timeline = Timeline(page);
        for (var index = timeline.Length - 1; index >= 0; index--)
        {
            _messages.Insert(0, timeline[index]);
        }

        C<Button>("OlderButton").IsVisible = page.HasMore;
        Feedback("Messages précédents chargés.");
    }

    private async Task CreateAsync()
    {
        if (C<ComboBox>("ModelSelector").SelectedItem is not string model)
        {
            Feedback("Sélectionnez un modèle Ollama local.");
            return;
        }

        var title = C<TextBox>("NewTitleInput").Text;
        var conversation = await _client.CreateAsync(string.IsNullOrWhiteSpace(title) ? "Nouvelle conversation" : title,
            model, C<TextBox>("SystemPromptInput").Text, _lifetime.Token);
        await RefreshConversationsAsync(conversation.Id);
        await LoadAsync(conversation);
        C<TextBox>("NewTitleInput").Text = "";
        C<TextBox>("MessageInput").Text = "";
        Feedback("Conversation créée. Vous pouvez discuter ou importer vos sources.");
        Dispatcher.UIThread.Post(() => C<TextBox>("MessageInput").Focus(), DispatcherPriority.Background);
    }

    private async Task RenameAsync()
    {
        if (_current is null)
        {
            return;
        }

        var conversation = await _client.RenameAsync(_current.Id, C<TextBox>("TitleInput").Text ?? "", _lifetime.Token);
        await RefreshConversationsAsync(conversation.Id);
        C<TextBox>("TitleInput").Text = conversation.Title;
        Feedback("Conversation renommée.");
    }

    private async Task DeleteAsync()
    {
        if (_current is null)
        {
            return;
        }

        var dialog = new Window
        {
            Title = "Supprimer la conversation ?",
            Width = 440,
            Height = 200,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        var cancel = new Button { Content = "Conserver" };
        var confirm = new Button { Content = "Supprimer définitivement" };
        cancel.Click += (_, _) => dialog.Close(false);
        confirm.Click += (_, _) => dialog.Close(true);
        dialog.Content = new StackPanel
        {
            Margin = new Thickness(24),
            Spacing = 20,
            Children =
            {
                new TextBlock { Text = "La conversation, ses messages et ses fichiers seront supprimés.", TextWrapping = global::Avalonia.Media.TextWrapping.Wrap },
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Children = { cancel, confirm } }
            }
        };
        if (!await dialog.ShowDialog<bool>(this))
        {
            Feedback("Suppression annulée.");
            return;
        }

        await _client.DeleteAsync(_current.Id, _lifetime.Token);
        _drafts.Remove(_current.Id);
        _loadingDraft = true;
        C<TextBox>("MessageInput").Text = "";
        _loadingDraft = false;
        _current = null;
        _drafts.Save();
        await RefreshConversationsAsync(null);
        if (_current is not null)
        {
            await LoadAsync(_current);
        }

        Feedback("Conversation supprimée.");
    }

    private async Task SendAsync()
    {
        var text = C<TextBox>("MessageInput").Text?.Trim();
        if (_current is null || string.IsNullOrWhiteSpace(text))
        {
            Feedback("Saisissez un message.");
            return;
        }

        var pending = _drafts.Set(_current.Id, text);
        _drafts.Save();
        _pendingId = pending.RequestId;
        using var generation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _generation = generation;
        UpdateControls();
        Feedback($"Ollama prépare la réponse avec {_current.Model}… Le premier chargement peut prendre du temps.");
        var streamed = new System.Text.StringBuilder();
        var lastRender = DateTime.UtcNow;
        var hasThinking = false;
        C<Expander>("StreamingPanel").IsVisible = true;
        C<Expander>("StreamingPanel").Header = "Réponse provisoire · en attente d’enregistrement";
        C<MarkdownView>("StreamingText").Text = "";
        C<Expander>("StreamingTools").IsVisible = false;
        C<SelectableTextBlock>("StreamingToolText").Text = "";
        C<TextBlock>("StreamingStatus").Text = "Préparation du contexte et chargement du modèle…";
        C<Border>("PendingUserPanel").IsVisible = true;
        C<TextBlock>("PendingUserLabel").Text = "Vous · envoi en cours";
        C<SelectableTextBlock>("PendingUserText").Text = text;
        _followOutput = true; ScrollToEnd();
        try
        {
            var result = await _client.StreamMessageAsync(_current.Id, _pendingId, text, _current.Revision, delta =>
            {
                if (delta.Status is not null)
                { Feedback(delta.Status); C<TextBlock>("StreamingStatus").Text = delta.Status; }
                if (delta.Tool is { } tool)
                {
                    C<Expander>("StreamingTools").IsVisible = true;
                    C<SelectableTextBlock>("StreamingToolText").Text += ToolDisplay.Describe(tool) + "\n\n";
                }
                streamed.Append(delta.Content);
                hasThinking |= !string.IsNullOrEmpty(delta.Thinking);
                // Throttle layout work; avoid re-rendering the whole text for every token.
                if ((DateTime.UtcNow - lastRender).TotalMilliseconds >= 80 || streamed.Length == 0)
                {
                    C<MarkdownView>("StreamingText").Text = streamed.ToString();
                    if (delta.Status is null && streamed.Length > 0) C<TextBlock>("StreamingStatus").Text = $"Génération en cours · {streamed.Length:N0} caractères";
                    else if (delta.Status is null && hasThinking) C<TextBlock>("StreamingStatus").Text = "Le modèle réfléchit…";
                    lastRender = DateTime.UtcNow;
                }
                return Task.CompletedTask;
            }, generation.Token);
            // The server has confirmed persistence, so subsequent refresh failure
            // must not invite a duplicate send.
            C<TextBox>("MessageInput").Text = "";
            _drafts.Remove(_current.Id);
            _drafts.Save();
            C<Expander>("StreamingPanel").IsVisible = false;
            await RefreshConversationsAsync(_current.Id);
            if (_current is not null)
            {
                await LoadAsync(_current);
            }

            Feedback(result.Replayed ? "Échange déjà enregistré : aucun doublon ajouté."
                : $"Réponse enregistrée · {result.HistoryMessagesIncluded} messages et {result.FilesIncluded} extraits initiaux · "
                    + (result.Usage is { } usage ? $"{usage.Calls.Count} appels · {usage.InputTokens?.ToString() ?? "?"} tokens entrants cumulés · {usage.OutputTokens?.ToString() ?? "?"} sortants cumulés" : $"{result.Stats?.OutputTokens?.ToString() ?? "?"} tokens (dernier appel)")
                    + (result.Stats is { EvaluationNanoseconds: > 0, OutputTokens: { } count } stats
                        ? $" · {count * 1_000_000_000.0 / stats.EvaluationNanoseconds.Value:F1} tok/s" : "")
                    + (result.Stats?.DoneReason == "length" ? " · limite de sortie atteinte" : ""));
        }
        finally
        {
            if (C<Expander>("StreamingPanel").IsVisible)
            {
                C<MarkdownView>("StreamingText").Text = streamed.Length > 0 ? streamed.ToString() : "Aucune réponse reçue.";
                C<Expander>("StreamingPanel").Header = "Réponse provisoire · enregistrement non confirmé";
                C<TextBlock>("StreamingStatus").Text = "Génération interrompue · brouillon conservé";
                C<TextBlock>("PendingUserLabel").Text = "Vous · demande non confirmée";
            }
            _generation = null;
        }
    }

    private void SaveDraft()
    {
        if (_current is not null && !_loadingDraft)
            _drafts.Set(_current.Id, C<TextBox>("MessageInput").Text ?? "");
    }

    private void ScrollToEnd()
    {
        if (_followOutput) Dispatcher.UIThread.Post(() =>
        { if (_followOutput) C<ScrollViewer>("MessageScroll").ScrollToEnd(); }, DispatcherPriority.Background);
    }
}
