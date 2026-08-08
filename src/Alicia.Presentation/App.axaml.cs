using Alicia.Presentation.ViewModels;
using Alicia.Presentation.Views;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace Alicia.Presentation;

public partial class App : global::Avalonia.Application
{
    private static Func<MainViewModel>? _mainViewModelFactory;

    public static void ConfigureMainViewModelFactory(Func<MainViewModel> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        _mainViewModelFactory = factory;
    }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        MainViewModel viewModel = _mainViewModelFactory?.Invoke()
            ?? throw new InvalidOperationException("The platform host must configure the Alicia presentation runtime before startup.");

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow
            {
                DataContext = viewModel,
            };
        }
        else if (ApplicationLifetime is ISingleViewApplicationLifetime singleView)
        {
            singleView.MainView = new MainView
            {
                DataContext = viewModel,
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
