using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace Alicia.Presentation.Views;

public partial class ShellView : UserControl
{
    private static readonly TimeSpan _navigationOpenDelay = TimeSpan.FromMilliseconds(275);
    private static readonly TimeSpan _navigationCloseDelay = TimeSpan.FromMilliseconds(450);

    private bool _isPointerOverRail;
    private bool _isPointerOverFlyout;
    private int _openDelayGeneration;
    private int _closeDelayGeneration;

    public ShellView()
    {
        InitializeComponent();
    }

    private void OnNavigationRailPointerEntered(object? sender, PointerEventArgs e)
    {
        _isPointerOverRail = true;
        CancelCloseDelay();

        if (!NavigationFlyout.IsVisible)
        {
            ScheduleOpenDelay();
        }
    }

    private void OnNavigationRailPointerExited(object? sender, PointerEventArgs e)
    {
        _isPointerOverRail = false;
        CancelOpenDelay();
        ScheduleCloseIfNavigationInactive();
    }

    private void OnNavigationFlyoutPointerEntered(object? sender, PointerEventArgs e)
    {
        _isPointerOverFlyout = true;
        CancelCloseDelay();
    }

    private void OnNavigationFlyoutPointerExited(object? sender, PointerEventArgs e)
    {
        _isPointerOverFlyout = false;
        ScheduleCloseIfNavigationInactive();
    }

    private void OnNavigationGotFocus(object? sender, RoutedEventArgs e)
    {
        CancelOpenDelay();
        CancelCloseDelay();
        NavigationFlyout.IsVisible = true;
    }

    private void OnNavigationLostFocus(object? sender, RoutedEventArgs e)
    {
        ScheduleCloseIfNavigationInactive();
    }

    private void OnNavigationKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape || !NavigationFlyout.IsVisible)
        {
            return;
        }

        CancelOpenDelay();
        CancelCloseDelay();
        NavigationFlyout.IsVisible = false;
        e.Handled = true;
    }

    private void OnUnloaded(object? sender, RoutedEventArgs e)
    {
        CancelOpenDelay();
        CancelCloseDelay();
        NavigationFlyout.IsVisible = false;
    }

    private void ScheduleOpenDelay()
    {
        int generation = ++_openDelayGeneration;
        _ = OpenNavigationAfterDelayAsync(generation);
    }

    private void ScheduleCloseIfNavigationInactive()
    {
        if (_isPointerOverRail || _isPointerOverFlyout)
        {
            return;
        }

        int generation = ++_closeDelayGeneration;
        _ = CloseNavigationAfterDelayAsync(generation);
    }

    private async Task OpenNavigationAfterDelayAsync(int generation)
    {
        await Task.Delay(_navigationOpenDelay).ConfigureAwait(false);

        Dispatcher.UIThread.Post(() =>
        {
            if (generation == _openDelayGeneration && _isPointerOverRail)
            {
                NavigationFlyout.IsVisible = true;
            }
        });
    }

    private async Task CloseNavigationAfterDelayAsync(int generation)
    {
        await Task.Delay(_navigationCloseDelay).ConfigureAwait(false);

        Dispatcher.UIThread.Post(() =>
        {
            if (generation == _closeDelayGeneration && !_isPointerOverRail && !_isPointerOverFlyout)
            {
                NavigationFlyout.IsVisible = false;
            }
        });
    }

    private void CancelOpenDelay()
    {
        _openDelayGeneration++;
    }

    private void CancelCloseDelay()
    {
        _closeDelayGeneration++;
    }
}
