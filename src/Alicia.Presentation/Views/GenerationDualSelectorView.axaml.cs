using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Alicia.Presentation.Views;

public partial class GenerationDualSelectorView : UserControl
{
    public GenerationDualSelectorView()
    {
        InitializeComponent();
    }

    private void OnInformationButtonClick(object? sender, RoutedEventArgs eventArgs)
    {
        _ = eventArgs;
        if (sender is Button button)
        {
            ToolTip.SetIsOpen(button, !ToolTip.GetIsOpen(button));
        }
    }

    internal void FocusInitialSelection()
    {
        if (ModelList.ItemCount > 0)
        {
            ModelList.Focus(NavigationMethod.Tab);
            if (ModelList.SelectedItem is object selectedItem)
            {
                ModelList.ScrollIntoView(selectedItem);
            }
        }
        else
        {
            CloseButton.Focus(NavigationMethod.Tab);
        }
    }
}
