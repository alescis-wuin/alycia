using System.Collections.Specialized;
using System.ComponentModel;
using Alicia.Presentation.ViewModels;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace Alicia.Presentation.Views;

public partial class MainView : UserControl
{
    private static readonly string[] _thinkingIndicatorFrames =
    [
        "Alicia réfléchit.",
        "Alicia réfléchit..",
        "Alicia réfléchit...",
    ];

    private bool _initialized;
    private bool _messageScrollPending;
    private MainViewModel? _subscribedViewModel;
    private readonly HashSet<MessageViewModel> _subscribedMessages = [];
    private readonly DispatcherTimer _thinkingIndicatorTimer;
    private int _thinkingIndicatorFrameIndex;

    public MainView()
    {
        InitializeComponent();

        _thinkingIndicatorTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(420),
        };
        _thinkingIndicatorTimer.Tick += OnThinkingIndicatorTick;

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

        UpdateThinkingIndicatorAnimation();
        ScrollMessagesToEnd();
    }

    private void OnUnloaded(object? sender, RoutedEventArgs eventArgs)
    {
        _ = sender;
        _ = eventArgs;
        _thinkingIndicatorTimer.Stop();
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

    private void OnMessageComposerShellPointerPressed(
        object? sender,
        PointerPressedEventArgs eventArgs)
    {
        if (!eventArgs.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        if (eventArgs.Source is StyledElement source)
        {
            for (StyledElement? current = source; current is not null; current = current.Parent)
            {
                if (current is Button)
                {
                    return;
                }

                if (ReferenceEquals(current, sender))
                {
                    break;
                }
            }
        }

        _ = MessageComposer.Focus();
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
        UpdateThinkingIndicatorAnimation();
        ScrollMessagesToEnd();
    }

    private void OnMessagePropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        _ = sender;

        if (string.Equals(
                eventArgs.PropertyName,
                nameof(MessageViewModel.Content),
                StringComparison.Ordinal)
            || string.Equals(
                eventArgs.PropertyName,
                nameof(MessageViewModel.ReasoningRevision),
                StringComparison.Ordinal))
        {
            ScrollMessagesToEnd();
        }

        if (string.Equals(
            eventArgs.PropertyName,
            nameof(MessageViewModel.IsWaitingForFirstDelta),
            StringComparison.Ordinal))
        {
            UpdateThinkingIndicatorAnimation();
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

    private void OnThinkingIndicatorTick(object? sender, EventArgs eventArgs)
    {
        _ = sender;
        _ = eventArgs;

        MessageViewModel? waitingMessage = GetWaitingStreamingMessage();

        if (waitingMessage is null)
        {
            UpdateThinkingIndicatorAnimation();
            return;
        }

        _thinkingIndicatorFrameIndex = (_thinkingIndicatorFrameIndex + 1)
            % _thinkingIndicatorFrames.Length;
        waitingMessage.SetThinkingIndicatorText(
            _thinkingIndicatorFrames[_thinkingIndicatorFrameIndex]);
    }

    private void UpdateThinkingIndicatorAnimation()
    {
        MessageViewModel? waitingMessage = GetWaitingStreamingMessage();

        if (waitingMessage is null)
        {
            _thinkingIndicatorTimer.Stop();
            _thinkingIndicatorFrameIndex = 0;
            return;
        }

        waitingMessage.SetThinkingIndicatorText(
            _thinkingIndicatorFrames[_thinkingIndicatorFrameIndex]);

        if (!_thinkingIndicatorTimer.IsEnabled)
        {
            _thinkingIndicatorTimer.Start();
        }
    }

    private MessageViewModel? GetWaitingStreamingMessage()
    {
        return _subscribedViewModel?.Messages
            .LastOrDefault(message => message.IsStreaming && message.IsWaitingForFirstDelta);
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
