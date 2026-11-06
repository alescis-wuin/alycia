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
    private readonly CancellationTokenSource _lifetime = new();
    private readonly ObservableCollection<ChatMessageItem> _messages = [];
    private CancellationTokenSource? _generation;
    private ConversationSummary? _current;
    private bool _busy;
    private Guid _pendingId;
    private Guid _pendingConversation;
    private string? _pendingText;

    public ChatWindow() : this(new ChatApiClient()) { }

    internal ChatWindow(ChatApiClient client)
    {
        _client = client;
        InitializeComponent();
        C<ItemsControl>("MessageList").ItemsSource = _messages;
        Opened += async (_, _) => await RunAsync(InitializeAsync);
        Closed += (_, _) => _lifetime.Cancel();
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
        UpdateControls();
    }

    private T C<T>(string name) where T : Control => this.FindControl<T>(name)
        ?? throw new InvalidOperationException($"Control {name} not found.");

    private void Feedback(string message) => C<TextBlock>("FeedbackText").Text = message;

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
            Feedback("Envoi interrompu. Votre texte est conservé. Actualisez l’historique avant de réessayer.");
        }
        catch (ChatApiException exception)
        {
            Feedback(exception.Message);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Chat operation failed: {exception.GetType().Name}");
            Feedback("Une erreur inattendue est survenue. Votre texte est conservé.");
        }
        finally
        {
            _busy = false;
            UpdateControls();
        }
    }

    private void UpdateControls()
    {
        C<StackPanel>("EmptyState").IsVisible = _messages.Count == 0;
        C<TextBlock>("EmptyHint").Text = _current is null
            ? "Choisissez un modèle et créez votre première conversation."
            : "Posez votre première question.";
        C<StackPanel>("CreationPanel").IsEnabled = !_busy;
        C<ListBox>("ConversationList").IsEnabled = !_busy;
        C<Button>("RefreshButton").IsEnabled = !_busy;
        C<Grid>("ConversationHeader").IsEnabled = !_busy && _current is not null;
        C<Button>("UploadButton").IsEnabled = !_busy && _current is not null;
        C<Button>("FolderButton").IsEnabled = !_busy && _current is not null;
        C<Button>("SendButton").IsEnabled = !_busy && _current is not null;
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
            ? "Aucun modèle local. Installez-en un avec ollama pull, puis actualisez."
            : "Prêt à discuter.");
    }

    private async Task RefreshConversationsAsync(Guid? selectId)
    {
        var conversations = await _client.ListAsync(_lifetime.Token);
        var list = C<ListBox>("ConversationList");
        list.ItemsSource = conversations;
        _current = conversations.FirstOrDefault(item => item.Id == selectId) ?? conversations.FirstOrDefault();
        list.SelectedItem = _current;
        if (_current is null)
        {
            _messages.Clear();
            C<TextBox>("TitleInput").Text = "";
            C<TextBlock>("ConversationDetails").Text = "Créez une conversation pour commencer.";
            C<Button>("OlderButton").IsVisible = false;
        }
    }

    private async Task LoadAsync(ConversationSummary conversation)
    {
        var page = await _client.MessagesAsync(conversation.Id, null, _lifetime.Token);
        _current = page.Conversation;
        C<TextBox>("TitleInput").Text = _current.Title;
        C<TextBlock>("ConversationDetails").Text = $"{_current.Model} · {_current.MessageCount} messages enregistrés";
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
        Feedback("Conversation créée.");
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
        await RefreshConversationsAsync(null);
        if (_current is not null)
        {
            await LoadAsync(_current);
        }

        C<TextBox>("MessageInput").Text = "";
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

        // Keep the same id after ambiguous network failure or cancellation.
        if (_pendingConversation != _current.Id || _pendingText != text)
        {
            _pendingId = Guid.NewGuid();
            _pendingConversation = _current.Id;
            _pendingText = text;
        }

        using var generation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _generation = generation;
        UpdateControls();
        Feedback("Ollama prépare la réponse… Le premier chargement du modèle peut prendre du temps.");
        try
        {
            var result = await _client.SendMessageAsync(_current.Id, _pendingId, text, generation.Token);
            // The server has confirmed persistence, so subsequent refresh failure
            // must not invite a duplicate send.
            C<TextBox>("MessageInput").Text = "";
            _pendingText = null;
            await RefreshConversationsAsync(_current.Id);
            if (_current is not null)
            {
                await LoadAsync(_current);
            }

            Feedback(result.Replayed ? "Échange déjà enregistré : aucun doublon ajouté."
                : $"Réponse enregistrée · {result.HistoryMessagesIncluded} messages et {result.FilesIncluded} fichiers dans le contexte.");
        }
        finally
        {
            _generation = null;
        }
    }

    private void ScrollToEnd() => Dispatcher.UIThread.Post(
        () => C<ScrollViewer>("MessageScroll").ScrollToEnd(), DispatcherPriority.Background);
}
