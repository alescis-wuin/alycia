using Alicia.Presentation.ViewModels;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;

[assembly: AvaloniaTestApplication(typeof(Alicia.Presentation.Tests.Ui.HeadlessTestAppBuilder))]

namespace Alicia.Presentation.Tests.Ui;

public static class HeadlessTestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp()
    {
        Alicia.Presentation.App.ConfigureShellViewModelFactory(
            () => new ShellViewModel(UiTestMainViewModelFactory.Create()));

        return AppBuilder
            .Configure<Alicia.Presentation.App>()
            .UseSkia()
            .WithInterFont()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions
            {
                UseHeadlessDrawing = false,
            });
    }
}
