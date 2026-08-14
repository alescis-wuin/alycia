using Alicia.Presentation.ViewModels;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;

namespace Alicia.Presentation.Views;

public partial class ConversationHistoryView : UserControl
{
    public static readonly StyledProperty<bool> FocusSearchWhenShownProperty =
        AvaloniaProperty.Register<ConversationHistoryView, bool>(nameof(FocusSearchWhenShown));

    private IInputElement? _previousFocus;

    public ConversationHistoryView()
    {
        InitializeComponent();
    }

    public bool FocusSearchWhenShown
    {
        get => GetValue(FocusSearchWhenShownProperty);
        set => SetValue(FocusSearchWhenShownProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property != IsVisibleProperty || !FocusSearchWhenShown)
        {
            return;
        }

        if (IsVisible)
        {
            FocusSearchInput();
        }
        else
        {
            RestorePreviousFocus();
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

    private void FocusSearchInput()
    {
        TopLevel? topLevel = TopLevel.GetTopLevel(this);

        if (topLevel is null)
        {
            return;
        }

        _previousFocus = topLevel.FocusManager.GetFocusedElement();
        Dispatcher.UIThread.Post(() =>
        {
            if (IsVisible && IsEffectivelyVisible && FocusSearchWhenShown)
            {
                _ = topLevel.FocusManager.Focus(HistorySearchBox);
            }
        });
    }

    private void RestorePreviousFocus()
    {
        IInputElement? previousFocus = _previousFocus;
        _previousFocus = null;

        if (previousFocus is null)
        {
            return;
        }

        TopLevel? topLevel = TopLevel.GetTopLevel(this);

        if (topLevel is null)
        {
            return;
        }

        Dispatcher.UIThread.Post(() => _ = topLevel.FocusManager.Focus(previousFocus));
    }
}
