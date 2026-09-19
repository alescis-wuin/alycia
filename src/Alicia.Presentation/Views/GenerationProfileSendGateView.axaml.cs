using Alicia.Presentation.ViewModels;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;

namespace Alicia.Presentation.Views;

public partial class GenerationProfileSendGateView : UserControl
{
    private IInputElement? _previousFocus;

    public GenerationProfileSendGateView()
    {
        InitializeComponent();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property != IsVisibleProperty)
        {
            return;
        }

        if (IsVisible)
        {
            FocusCancelAction();
        }
        else
        {
            RestorePreviousFocus();
        }
    }

    private void OnDialogKeyDown(object? sender, KeyEventArgs eventArgs)
    {
        _ = sender;

        if (eventArgs.Key != Key.Escape || DataContext is not MainViewModel viewModel)
        {
            return;
        }

        viewModel.CancelTransientActionCommand.Execute(null);
        eventArgs.Handled = true;
    }

    private void FocusCancelAction()
    {
        TopLevel? topLevel = TopLevel.GetTopLevel(this);

        if (topLevel is null)
        {
            return;
        }

        _previousFocus = topLevel.FocusManager.GetFocusedElement();
        Dispatcher.UIThread.Post(() =>
        {
            if (IsVisible && IsEffectivelyVisible)
            {
                _ = topLevel.FocusManager.Focus(CancelProfileSendGateButton);
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
