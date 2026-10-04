using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Alicia.Presentation.Views;

public partial class ProviderWorkspaceView : UserControl
{
    private bool? _isNarrow;

    public ProviderWorkspaceView()
    {
        InitializeComponent();
        SizeChanged += (_, _) => UpdateColumns();
    }

    private void UpdateColumns()
    {
        bool narrow = Bounds.Width < 1020;
        if (_isNarrow == narrow)
        {
            return;
        }

        _isNarrow = narrow;
        ProviderColumns.ColumnDefinitions = new ColumnDefinitions(narrow ? "*" : "*,24,1.15*");
        Grid.SetColumn(ProviderInformation, narrow ? 0 : 2);
        Grid.SetRow(ProviderInformation, narrow ? 1 : 0);
        ProviderInformation.Margin = new Avalonia.Thickness(0, narrow ? 20 : 0, 0, 0);
    }

    private void OnHelpClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button button)
        {
            ToolTip.SetIsOpen(button, !ToolTip.GetIsOpen(button));
        }
    }

    private void OnHelpKeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is Button button && e.Key == Key.Escape && ToolTip.GetIsOpen(button))
        {
            ToolTip.SetIsOpen(button, false);
            e.Handled = true;
        }
    }

    private void OnHelpLostFocus(object? sender, RoutedEventArgs e)
    {
        if (sender is Button button)
        {
            ToolTip.SetIsOpen(button, false);
        }
    }
}
