using System.Collections.Specialized;
using System.ComponentModel;
using Alicia.Presentation.ViewModels;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace Alicia.Presentation.Views;

public partial class MainView : UserControl
{
    private bool _initialized;
    private bool _messageScrollPending;
    private MainViewModel? _subscribedViewModel;
    private readonly HashSet<MessageViewModel> _subscribedMessages = [];

    public MainView()
    {
        InitializeComponent();

        MessageComposer.AddHandler(
            InputElement.KeyDownEvent,
            OnMessageComposerKeyDown,
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
    }

    private async void OnLoaded(object? sender, RoutedEventArgs eventArgs)
    {
        _ = sender;
        _ = eventArgs;

        if (DataContext is not MainViewModel viewModel)
        {
            return;
        }

        SubscribeToMessages(viewModel);

        if (!_initialized)
        {
            _initialized = true;
            await viewModel.InitializeAsync().ConfigureAwait(true);
        }

        ScrollMessagesToEnd();
    }

    private void OnUnloaded(object? sender, RoutedEventArgs eventArgs)
    {
        _ = sender;
        _ = eventArgs;
        UnsubscribeFromMessages();
    }

    private async void OnConversationPreviewPointerEntered(
        object? sender,
        PointerEventArgs eventArgs)
    {
        _ = eventArgs;

        if (sender is not Control { DataContext: ConversationListItemViewModel item })
        {
            return;
        }

        await item.EnsurePreviewLoadedAsync().ConfigureAwait(true);
    }

    private async void OnMessageComposerKeyDown(object? sender, KeyEventArgs eventArgs)
    {
        _ = sender;

        if (eventArgs.Key != Key.Enter || eventArgs.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            return;
        }

        eventArgs.Handled = true;
        await ExecuteSendMessageAsync().ConfigureAwait(true);
    }

    private async void OnSendMessageClick(object? sender, RoutedEventArgs eventArgs)
    {
        _ = sender;
        _ = eventArgs;
        await ExecuteSendMessageAsync().ConfigureAwait(true);
    }

    private async Task ExecuteSendMessageAsync()
    {
        if (DataContext is not MainViewModel viewModel
            || !viewModel.SendMessageCommand.CanExecute(null))
        {
            return;
        }

        await viewModel.SendMessageCommand.ExecuteAsync(null).ConfigureAwait(true);

        ScrollMessagesToEnd();

        if (viewModel.HasSelectedConversation)
        {
            _ = MessageComposer.Focus();
        }
    }

    private void SubscribeToMessages(MainViewModel viewModel)
    {
        if (ReferenceEquals(_subscribedViewModel, viewModel))
        {
            return;
        }

        UnsubscribeFromMessages();
        _subscribedViewModel = viewModel;
        _subscribedViewModel.Messages.CollectionChanged += OnMessagesCollectionChanged;
        SynchronizeMessageSubscriptions();
    }

    private void UnsubscribeFromMessages()
    {
        if (_subscribedViewModel is not null)
        {
            _subscribedViewModel.Messages.CollectionChanged -= OnMessagesCollectionChanged;
            _subscribedViewModel = null;
        }

        foreach (MessageViewModel message in _subscribedMessages)
        {
            message.PropertyChanged -= OnMessagePropertyChanged;
        }

        _subscribedMessages.Clear();
    }

    private void OnMessagesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs eventArgs)
    {
        _ = sender;
        _ = eventArgs;
        SynchronizeMessageSubscriptions();
        ScrollMessagesToEnd();
    }

    private void OnMessagePropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        _ = sender;

        if (string.Equals(
            eventArgs.PropertyName,
            nameof(MessageViewModel.Content),
            StringComparison.Ordinal))
        {
            ScrollMessagesToEnd();
        }
    }

    private void SynchronizeMessageSubscriptions()
    {
        if (_subscribedViewModel is null)
        {
            return;
        }

        foreach (MessageViewModel message in _subscribedMessages)
        {
            message.PropertyChanged -= OnMessagePropertyChanged;
        }

        _subscribedMessages.Clear();

        foreach (MessageViewModel message in _subscribedViewModel.Messages)
        {
            message.PropertyChanged += OnMessagePropertyChanged;
            _subscribedMessages.Add(message);
        }
    }

    private void ScrollMessagesToEnd()
    {
        if (_messageScrollPending)
        {
            return;
        }

        _messageScrollPending = true;
        Dispatcher.UIThread.Post(
            () =>
            {
                _messageScrollPending = false;
                MessagesScrollViewer.ScrollToEnd();
            },
            DispatcherPriority.Background);
    }
}
