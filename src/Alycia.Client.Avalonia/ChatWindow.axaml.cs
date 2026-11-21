using Alycia.Contracts.Chat;
using Alycia.Client.Avalonia.Presentation.Conversations;
using Alycia.Client.Avalonia.Presentation.Navigation;
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
    private readonly ConversationComposer _composer;
    private readonly ProjectNavigation _navigation;
    private readonly DispatcherTimer _draftTimer = new() { Interval = TimeSpan.FromMilliseconds(750) };
    private bool _loadingDraft;
    private string _searchQuery => _navigation.SearchQuery;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly ConversationTimeline _timeline = new();
    private ReadOnlyObservableCollection<ChatMessageItem> _messages => _timeline.Messages;
    private CancellationTokenSource? _generation;
    private ConversationSummary? _current { get => _navigation.Conversation; set => _navigation.Conversation = value; }
    private bool _busy;
    private bool _followOutput = true;
    private bool _sidebarManual;

    public ChatWindow() : this(new ChatApiClient()) { }

    internal ChatWindow(ChatApiClient client, DraftStore? drafts = null)
    {
        _client = client;
        _composer = new(drafts ?? new DraftStore());
        _navigation = new(client);
        InitializeComponent();
        _navigation.OwnerChanged += ResetOwnerView;
        C<ItemsControl>("MessageList").ItemsSource = _messages;
        Opened += async (_, _) => await RunAsync(InitializeAsync);
        Closed += (_, _) => { _draftTimer.Stop(); SaveDraft(); _composer.Flush(); _lifetime.Cancel(); };
        _draftTimer.Tick += (_, _) => { _draftTimer.Stop(); _composer.Flush(); };
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
        InitializeNavigation();
        InitializeIndexing();
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
        SizeChanged += (_, _) => { if (!_sidebarManual) C<Border>("Sidebar").IsVisible = Bounds.Width >= 1040; LayoutInspector(); };
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
            if (e.Key == Key.Escape && C<WrapPanel>("AttachmentActions").IsVisible) { C<WrapPanel>("AttachmentActions").IsVisible = false; C<Button>("UploadButton").Focus(); e.Handled = true; return; }
            if (e.Key == Key.Escape && C<Border>("Inspector").IsVisible) { HideInspector(); e.Handled = true; return; }
            if (e.Key == Key.Escape && C<Border>("ActionMenu").IsVisible) { C<Border>("ActionMenu").IsVisible = false; C<Button>("MoreButton").Focus(); e.Handled = true; return; }
            if (e.Key == Key.Escape && _generation is not null) { _generation.Cancel(); e.Handled = true; }
            if (!e.KeyModifiers.HasFlag(KeyModifiers.Control)) return;
            if (e.Key == Key.F && e.KeyModifiers.HasFlag(KeyModifiers.Shift)) { e.Handled = true; ToggleConcentration(); return; }
            if (e.Key == Key.N) { e.Handled = true; await RunAsync(CreateAsync); }
            if (e.Key == Key.F) { e.Handled = true; _sidebarManual = true; C<Border>("Sidebar").IsVisible = true; C<TextBox>("SearchInput").Focus(); }
        };
        UpdateControls();
    }

    private T C<T>(string name) where T : Control => this.FindControl<T>(name)
        ?? throw new InvalidOperationException($"Control {name} not found.");

    private void Feedback(string message, bool error = false)
    {
        var inspectorMessage = C<TextBlock>("InspectorFeedback");
        inspectorMessage.Text = error ? message : "";
        inspectorMessage.IsVisible = error && C<Border>("Inspector").IsVisible;
        C<TextBlock>("FeedbackText").Text = message;
        C<TextBlock>("FeedbackText").Foreground = Appearance.Color(this, error ? "Error" : "Muted");
        C<TextBlock>("FeedbackText").IsVisible = !string.IsNullOrWhiteSpace(message);
    }

    private async Task RunAsync(Func<Task> action)
    {
        if (_busy || _lifetime.IsCancellationRequested)
        {
            return;
        }

        _busy = true;
        C<WrapPanel>("AttachmentActions").IsVisible = false;
        C<Border>("ActionMenu").IsVisible = false;
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
        C<Button>("NewButton").IsEnabled = !_busy && !_project.Archived;
        C<Button>("ActiveModelButton").IsVisible = _current is not null;
        C<Button>("ActiveModelButton").IsEnabled = !_busy && _current is not null;
        C<TextBlock>("EmptyHint").Text = _current is null
            ? "Créez une conversation pour commencer."
            : "Posez votre première question.";
        C<StackPanel>("CreationPanel").IsEnabled = !_busy;
        C<WrapPanel>("ConversationTools").IsEnabled = !_busy && _current is not null;
        C<Button>("RegenerateButton").IsEnabled = !_busy && _current is { MessageCount: >= 2 };
        foreach (var name in new[] { "SearchButton", "MoreConversationsButton", "PromptsButton", "ImportConversationButton" })
            C<Button>(name).IsEnabled = !_busy;
        C<ListBox>("ConversationList").IsEnabled = !_busy;
        C<ComboBox>("ProjectSelector").IsEnabled = !_busy;
        C<Button>("RefreshButton").IsEnabled = !_busy;
        C<Button>("ContextButton").IsEnabled = !_busy && _current is not null;
        C<Button>("MoreButton").IsEnabled = !_busy && _current is not null;
        C<Button>("SendButton").IsVisible = _generation is null;
        C<Button>("FilesButton").IsEnabled = !_busy && (_current is not null || _project.Id.HasValue);
        C<Button>("InstructionsButton").IsEnabled = !_busy && _project.Id.HasValue;
        C<Button>("UploadButton").IsEnabled = !_busy && _current is not null;
        C<Button>("AttachProjectCommand").IsVisible = _project.Id.HasValue && !_project.Archived;
        C<Button>("FolderButton").IsEnabled = !_busy && _current is not null;
        C<Button>("SendButton").IsEnabled = !_busy && _current is not null && !string.IsNullOrWhiteSpace(C<TextBox>("MessageInput").Text);
        C<TextBox>("MessageInput").IsEnabled = !_busy && _current is not null;
        C<Button>("OlderButton").IsEnabled = !_busy;
        C<ProgressBar>("BusyIndicator").IsVisible = _busy;
        C<Button>("CancelButton").IsVisible = _generation is not null;
    }

    private async Task InitializeAsync()
    {
        await RefreshProjectsAsync(_project.Id);
        // Load persisted history even when Ollama is temporarily unavailable.
        await RefreshConversationsAsync(_current?.Id);
        if (_current is not null)
        {
            await LoadAsync(_current);
        }

        var models = await _client.ModelsAsync(_lifetime.Token);
        var selector = C<ComboBox>("ModelSelector");
        var selected = selector.SelectedItem as string ?? Appearance.Current.DefaultModel;
        selector.ItemsSource = models;
        selector.SelectedItem = models.Contains(selected) ? selected : models.FirstOrDefault();
        Feedback(models.Length == 0
            ? "Aucun modèle génératif local. Les modèles d’embeddings ne peuvent pas discuter. Installez un modèle génératif avec ollama pull, puis actualisez."
            : _composer.Error ?? "");
    }

    private async Task RefreshConversationsAsync(Guid? selectId)
    {
        SaveDraft();
        var page = await _navigation.SearchAsync(C<TextBox>("SearchInput").Text ?? "", selectId, _lifetime.Token);
        if (page is null) return;
        C<Button>("MoreConversationsButton").IsVisible = page.HasMore;
        var list = C<ListBox>("ConversationList");
        list.ItemsSource = page.Items;
        list.SelectedItem = _current;
        ShowDraft();
    }

    private void ResetOwnerView()
    {
        HideInspector();
        _inspectorOrigin = null;
        _evidence = null;
        _visibleEvidence = null;
        C<StackPanel>("InspectorBody").Children.Clear();
        C<StackPanel>("PageBody").Children.Clear();
        _timeline.Clear();
        ShowDraft();
        C<Expander>("StreamingPanel").IsVisible = false;
        C<Border>("PendingUserPanel").IsVisible = false;
        C<MarkdownView>("StreamingText").Text = "";
        C<Expander>("StreamingTools").IsVisible = false;
        C<SelectableTextBlock>("StreamingToolText").Text = "";
        C<SelectableTextBlock>("PendingUserText").Text = "";
        C<TextBox>("TitleInput").Text = _current?.Title ?? "";
        C<Button>("ActiveModelButton").Content = _current?.Model ?? "";
        C<TextBlock>("ConversationDetails").Text = "";
        C<Button>("OlderButton").IsVisible = false;
        ShowChat();
    }

    private async Task LoadAsync(ConversationSummary conversation, bool keepReading = false)
    {
        bool follow = !keepReading || _followOutput;
        var previousOffset = C<ScrollViewer>("MessageScroll").Offset;
        SaveDraft();
        HideInspector();
        _evidence = null;
        _current = conversation;
        var page = await _navigation.ReadMessagesAsync(null, _lifetime.Token);
        if (page is null) return;
        _current = page.Conversation;
        ShowDraft();
        C<Expander>("StreamingPanel").IsVisible = false;
        C<Border>("PendingUserPanel").IsVisible = false;
        _followOutput = follow; C<Button>("LatestButton").IsVisible = !follow;
        C<TextBox>("TitleInput").Text = _current.Title;
        ShowChat();
        C<Button>("ActiveModelButton").Content = _current.Model;
        C<TextBlock>("ConversationDetails").Text = $"{_current.MessageCount} messages";
        _timeline.Replace(page);

        C<Button>("OlderButton").IsVisible = page.HasMore;
        if (follow) ScrollToEnd();
        else Dispatcher.UIThread.Post(() => C<ScrollViewer>("MessageScroll").Offset = previousOffset, DispatcherPriority.Background);
        Feedback("");
    }

    private async Task LoadOlderAsync()
    {
        if (_current is null || _timeline.OldestSequence is not int before)
        {
            return;
        }

        var page = await _navigation.ReadMessagesAsync(before, _lifetime.Token);
        if (page is null) return;
        var scroll = C<ScrollViewer>("MessageScroll");
        var offset = scroll.Offset;
        double oldHeight = scroll.Extent.Height;
        _timeline.Prepend(page);

        C<Button>("OlderButton").IsVisible = page.HasMore;
        Dispatcher.UIThread.Post(() => scroll.Offset = new Vector(offset.X, offset.Y + Math.Max(0, scroll.Extent.Height - oldHeight)), DispatcherPriority.Background);
        Feedback("Messages précédents chargés.");
    }

    private async Task CreateAsync()
    {
        if (C<ComboBox>("ModelSelector").SelectedItem is not string model)
        {
            Feedback("Sélectionnez un modèle Ollama local.");
            return;
        }

        var conversation = await _navigation.CreateConversationAsync(C<TextBox>("NewTitleInput").Text, model, C<TextBox>("SystemPromptInput").Text, _lifetime.Token);
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
        ShowChat();
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
        _composer.Remove(_current.Id);
        ShowDraft();
        _current = null;
        _composer.Flush();
        await RefreshConversationsAsync(null);
        if (_current is not null)
        {
            await LoadAsync(_current);
        }

        Feedback("Conversation supprimée.");
    }

    private async Task SendAsync()
    {
        var submission = _composer.Begin(_current, C<TextBox>("MessageInput").Text);
        if (submission is null)
        {
            Feedback("Saisissez un message.");
            return;
        }

        using var generation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _generation = generation;
        UpdateControls();
        Feedback("");
        C<Expander>("StreamingPanel").IsVisible = true;
        C<Expander>("StreamingPanel").Header = "Réponse provisoire · en attente d’enregistrement";
        C<MarkdownView>("StreamingText").Text = "";
        C<Expander>("StreamingTools").IsVisible = false;
        C<SelectableTextBlock>("StreamingToolText").Text = "";
        C<TextBlock>("StreamingStatus").Text = "Préparation du contexte et chargement du modèle…";
        C<Border>("PendingUserPanel").IsVisible = true;
        C<TextBlock>("PendingUserLabel").Text = "Vous · envoi en cours";
        C<SelectableTextBlock>("PendingUserText").Text = submission.Draft.Text;
        _followOutput = true; ScrollToEnd();
        try
        {
            var result = await _composer.SendAsync(_client, delta =>
            {
                if (delta.Status is not null)
                { C<TextBlock>("StreamingStatus").Text = delta.Status; }
                if (delta.Tool is { } tool)
                {
                    C<Expander>("StreamingTools").IsVisible = true;
                    C<SelectableTextBlock>("StreamingToolText").Text += ToolDisplay.Describe(tool) + "\n\n";
                }
                if (submission.Append(delta))
                {
                    C<MarkdownView>("StreamingText").Text = submission.Output;
                    if (delta.Status is null && submission.GenerationStatus is { } status) C<TextBlock>("StreamingStatus").Text = status;
                }
                return Task.CompletedTask;
            }, generation.Token);
            C<TextBox>("MessageInput").Text = "";
            C<Expander>("StreamingPanel").IsVisible = false;
            await RefreshConversationsAsync(submission.Conversation.Id);
            if (_current is not null)
            {
                await LoadAsync(_current, keepReading: true);
            }

            Feedback(result.Stats?.DoneReason == "length" ? "Limite de sortie atteinte." : "");
        }
        finally
        {
            if (!submission.Confirmed && C<Expander>("StreamingPanel").IsVisible)
            {
                var editor = C<TextBox>("MessageInput");
                if (_composer.RestoreIfEmpty(_current?.Id, editor.Text) is { } restored) editor.Text = restored;
                var output = submission.Output;
                C<MarkdownView>("StreamingText").Text = output.Length > 0 ? output : "Aucune réponse reçue.";
                C<Expander>("StreamingPanel").Header = "Réponse provisoire · enregistrement non confirmé";
                C<TextBlock>("StreamingStatus").Text = "Génération interrompue · brouillon conservé";
                C<TextBlock>("PendingUserLabel").Text = "Vous · demande non confirmée";
            }
            _composer.End();
            _generation = null;
        }
    }

    private void ShowDraft()
    {
        _loadingDraft = true;
        try { C<TextBox>("MessageInput").Text = _composer.TextFor(_current?.Id); }
        finally { _loadingDraft = false; }
    }

    private void SaveDraft()
    {
        if (!_loadingDraft && _generation is null)
            _composer.Edit(_current?.Id, C<TextBox>("MessageInput").Text);
    }

    private void ScrollToEnd()
    {
        if (_followOutput) Dispatcher.UIThread.Post(() =>
        { if (_followOutput) C<ScrollViewer>("MessageScroll").ScrollToEnd(); }, DispatcherPriority.Background);
    }
}
