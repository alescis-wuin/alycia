using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace Alicia.Presentation.Controls;

public sealed class InlineRenameTextBox : TextBox
{
    protected override Type StyleKeyOverride => typeof(TextBox);

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == IsVisibleProperty && IsVisible)
        {
            Dispatcher.UIThread.Post(FocusAndSelectAll);
        }
    }

    private void FocusAndSelectAll()
    {
        if (!IsVisible || !IsEffectivelyVisible || !IsEnabled)
        {
            return;
        }

        _ = Focus();
        SelectAll();
    }
}
