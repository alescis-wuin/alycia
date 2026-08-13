using System.Collections.Specialized;
using System.ComponentModel;
using Alicia.Presentation.State;
using Alicia.Presentation.ViewModels;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace Alicia.Presentation.Views;

public partial class MainView : UserControl
{
    private const double NarrowConversationLayoutWidth = 780d;

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
    private readonly DispatcherTimer _scrollPersistenceTimer;
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

        _scrollPersistenceTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(300),
        };
        _scrollPersistenceTimer.Tick += OnScrollPersistenceTimerTick;

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
        viewModel.SetConversationHistoryNarrowLayout(
            Bounds.Width <= NarrowConversationLayoutWidth);

        if (!_initialized)
        {
            _initialized = true;
            await viewModel.InitializeAsync().ConfigureAwait(true);
        }

        UpdateThinkingIndicatorAnimation();
        RestoreConversationScrollPosition();
    }

    private async void OnUnloaded(object? sender, RoutedEventArgs eventArgs)
    {
        _ = sender;
        _ = eventArgs;
        _thinkingIndicatorTimer.Stop();
        _scrollPersistenceTimer.Stop();

        if (_subscribedViewModel is MainViewModel viewModel)
        {
            await viewModel.PersistConversationUiStateAsync().ConfigureAwait(true);
        }

        UnsubscribeFromMessages();
    }

    private void OnMainViewSizeChanged(object? sender, SizeChangedEventArgs eventArgs)
    {
        _ = sender;

        if (DataContext is MainViewModel viewModel)
        {
            viewModel.SetConversationHistoryNarrowLayout(
                eventArgs.NewSize.Width <= NarrowConversationLayoutWidth);
        }
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
        ScrollMessagesIfFollowing();

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
        _subscribedViewModel.PropertyChanged += OnViewModelPropertyChanged;
        _subscribedViewModel.Messages.CollectionChanged += OnMessagesCollectionChanged;
        SynchronizeMessageSubscriptions();
    }

    private void UnsubscribeFromMessages()
    {
        if (_subscribedViewModel is not null)
        {
            _subscribedViewModel.PropertyChanged -= OnViewModelPropertyChanged;
            _subscribedViewModel.Messages.CollectionChanged -= OnMessagesCollectionChanged;
            _subscribedViewModel = null;
        }

        foreach (MessageViewModel message in _subscribedMessages)
        {
            message.PropertyChanged -= OnMessagePropertyChanged;
        }

        _subscribedMessages.Clear();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        _ = sender;

        if (string.Equals(
            eventArgs.PropertyName,
            nameof(MainViewModel.ConversationScrollRestoreRevision),
            StringComparison.Ordinal))
        {
            RestoreConversationScrollPosition();
        }
    }

    private void OnMessagesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs eventArgs)
    {
        _ = sender;
        _ = eventArgs;
        SynchronizeMessageSubscriptions();
        UpdateThinkingIndicatorAnimation();
        ScrollMessagesIfFollowing();
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
            ScrollMessagesIfFollowing();
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

        if (_subscribedViewModel?.IsReducedMotionEnabled == true)
        {
            _thinkingIndicatorTimer.Stop();
            _thinkingIndicatorFrameIndex = 0;
            waitingMessage.SetThinkingIndicatorText("Alicia réfléchit…");
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

    private void OnMessagesScrollChanged(object? sender, ScrollChangedEventArgs eventArgs)
    {
        _ = sender;

        if (_subscribedViewModel is not MainViewModel viewModel
            || !viewModel.HasSelectedConversation)
        {
            return;
        }

        bool layoutChanged = eventArgs.ExtentDelta.X != 0
            || eventArgs.ExtentDelta.Y != 0
            || eventArgs.ViewportDelta.X != 0
            || eventArgs.ViewportDelta.Y != 0;

        if (layoutChanged)
        {
            ScrollMessagesIfFollowing();
            return;
        }

        double maximumVerticalOffset = Math.Max(
            0,
            MessagesScrollViewer.Extent.Height - MessagesScrollViewer.Viewport.Height);
        bool userMovedUp = eventArgs.OffsetDelta.Y < -0.5;

        viewModel.ReportConversationScrollPosition(
            MessagesScrollViewer.Offset.Y,
            maximumVerticalOffset,
            userMovedUp);
        ScheduleScrollPersistence();
    }

    private void ScheduleScrollPersistence()
    {
        _scrollPersistenceTimer.Stop();
        _scrollPersistenceTimer.Start();
    }

    private async void OnScrollPersistenceTimerTick(object? sender, EventArgs eventArgs)
    {
        _ = sender;
        _ = eventArgs;
        _scrollPersistenceTimer.Stop();

        if (_subscribedViewModel is MainViewModel viewModel)
        {
            await viewModel.PersistConversationUiStateAsync().ConfigureAwait(true);
        }
    }

    private void ScrollMessagesIfFollowing()
    {
        if (_subscribedViewModel?.CurrentConversationScrollMode
            == ConversationScrollMode.Following)
        {
            ScrollMessagesToEnd();
        }
    }

    private void RestoreConversationScrollPosition()
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

                if (_subscribedViewModel is not MainViewModel viewModel
                    || !viewModel.HasSelectedConversation)
                {
                    return;
                }

                if (viewModel.CurrentConversationScrollMode == ConversationScrollMode.Following)
                {
                    MessagesScrollViewer.ScrollToEnd();
                    return;
                }

                double maximumVerticalOffset = Math.Max(
                    0,
                    MessagesScrollViewer.Extent.Height - MessagesScrollViewer.Viewport.Height);
                MessagesScrollViewer.Offset = new Vector(
                    MessagesScrollViewer.Offset.X,
                    Math.Min(viewModel.CurrentConversationScrollOffset, maximumVerticalOffset));
            },
            DispatcherPriority.Background);
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

                if (_subscribedViewModel?.CurrentConversationScrollMode
                    == ConversationScrollMode.Following)
                {
                    MessagesScrollViewer.ScrollToEnd();
                }
            },
            DispatcherPriority.Background);
    }
}
