using Alicia.Presentation.ViewModels;
using Alicia.Presentation.Views;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace Alicia.Presentation;

public partial class App : global::Avalonia.Application
{
    private static Func<ShellViewModel>? _shellViewModelFactory;

    public static void ConfigureShellViewModelFactory(Func<ShellViewModel> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        _shellViewModelFactory = factory;
    }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        ShellViewModel viewModel = _shellViewModelFactory?.Invoke()
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
            singleView.MainView = new ShellView
            {
                DataContext = viewModel,
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
