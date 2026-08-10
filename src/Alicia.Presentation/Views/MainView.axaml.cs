using System.Collections.Specialized;
using Alicia.Presentation.ViewModels;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace Alicia.Presentation.Views;

public partial class MainView : UserControl
{
    private bool _initialized;
    private MainViewModel? _subscribedViewModel;

    public MainView()
    {
        InitializeComponent();
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
    }

    private void UnsubscribeFromMessages()
    {
        if (_subscribedViewModel is null)
        {
            return;
        }

        _subscribedViewModel.Messages.CollectionChanged -= OnMessagesCollectionChanged;
        _subscribedViewModel = null;
    }

    private void OnMessagesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs eventArgs)
    {
        _ = sender;
        _ = eventArgs;
        ScrollMessagesToEnd();
    }

    private void ScrollMessagesToEnd()
    {
        Dispatcher.UIThread.Post(
            MessagesScrollViewer.ScrollToEnd,
            DispatcherPriority.Background);
    }
}
