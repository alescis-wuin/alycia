using Alicia.Infrastructure.Conversations;
using Alicia.Presentation;
using Alicia.Presentation.ViewModels;
using Avalonia;

namespace Alicia.Desktop;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        App.ConfigureMainViewModelFactory(CreateMainViewModel);
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace();
    }

    private static MainViewModel CreateMainViewModel()
    {
        string localApplicationData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string storageDirectory = Path.Combine(localApplicationData, "Alicia", "conversations");
        LocalConversationRuntime runtime = LocalConversationRuntime.Create(storageDirectory);

        return new MainViewModel(
            runtime.CreateConversation,
            runtime.AppendMessage,
            runtime.LoadConversation,
            runtime.ListConversations,
            runtime.RenameConversation,
            runtime.DeleteConversation);
    }
}
