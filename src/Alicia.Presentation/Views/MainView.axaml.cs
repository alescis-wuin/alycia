using Alicia.Presentation.ViewModels;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Alicia.Presentation.Views;

public partial class MainView : UserControl
{
    private bool _initialized;

    public MainView()
    {
        InitializeComponent();
    }

    private async void OnLoaded(object? sender, RoutedEventArgs eventArgs)
    {
        _ = sender;
        _ = eventArgs;

        if (_initialized || DataContext is not MainViewModel viewModel)
        {
            return;
        }

        _initialized = true;
        await viewModel.InitializeAsync().ConfigureAwait(true);
    }
}
