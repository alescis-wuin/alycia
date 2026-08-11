using Alicia.Application.Conversations;
using Alicia.Infrastructure.Conversations;
using Alicia.Infrastructure.Providers.LlamaCpp;
using Alicia.Presentation;
using Alicia.Presentation.ViewModels;
using Avalonia;

namespace Alicia.Desktop;

internal static class Program
{
    private static LlamaCppProviderRuntime? _llamaCppRuntime;

    [STAThread]
    public static void Main(string[] args)
    {
        App.ConfigureMainViewModelFactory(CreateMainViewModel);

        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        finally
        {
            if (_llamaCppRuntime is not null)
            {
                _llamaCppRuntime.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
        }
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
        string providerDirectory = Path.Combine(
            localApplicationData,
            "Alicia",
            "providers",
            "llama.cpp");
        TimeProvider timeProvider = TimeProvider.System;
        LocalConversationRuntime runtime = LocalConversationRuntime.Create(
            storageDirectory,
            timeProvider);
        _llamaCppRuntime ??= new LlamaCppProviderRuntime(
            providerDirectory,
            timeProvider);
        StreamConversationTurnUseCase streamConversationTurn = new(
            runtime.Repository,
            _llamaCppRuntime,
            timeProvider);

        return new MainViewModel(
            runtime.CreateConversation,
            runtime.AppendMessage,
            streamConversationTurn,
            runtime.LoadConversation,
            runtime.ListConversations,
            runtime.RenameConversation,
            runtime.DeleteConversation,
            _llamaCppRuntime);
    }
}
