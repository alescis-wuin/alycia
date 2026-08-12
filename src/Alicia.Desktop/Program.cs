using Alicia.Application.Conversations;
using Alicia.Application.Providers;
using Alicia.Infrastructure.Conversations;
using Alicia.Infrastructure.Providers;
using Alicia.Infrastructure.Providers.LlamaCpp;
using Alicia.Presentation;
using Alicia.Presentation.ViewModels;
using Avalonia;

namespace Alicia.Desktop;

internal static class Program
{
    private static LlamaCppProviderRuntime? _llamaCppRuntime;
    private static JsonInferenceProviderConfigurationStore? _providerConfigurationStore;

    [STAThread]
    public static void Main(string[] args)
    {
        App.ConfigureShellViewModelFactory(CreateShellViewModel);

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

            _providerConfigurationStore?.Dispose();
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

    private static ShellViewModel CreateShellViewModel()
    {
        return new ShellViewModel(CreateMainViewModel());
    }

    private static MainViewModel CreateMainViewModel()
    {
        string localApplicationData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string applicationDirectory = Path.Combine(localApplicationData, "Alicia");
        string storageDirectory = Path.Combine(applicationDirectory, "conversations");
        string providersDirectory = Path.Combine(applicationDirectory, "providers");
        string llamaCppDirectory = Path.Combine(providersDirectory, "llama.cpp");
        string providerConfigurationPath = Path.Combine(
            providersDirectory,
            "configuration.json");
        string legacyLlamaCppSettingsPath = Path.Combine(
            llamaCppDirectory,
            "settings.json");
        TimeProvider timeProvider = TimeProvider.System;
        LocalConversationRuntime runtime = LocalConversationRuntime.Create(
            storageDirectory,
            timeProvider);
        _llamaCppRuntime ??= new LlamaCppProviderRuntime(
            llamaCppDirectory,
            timeProvider);

        InferenceProviderDescriptor llamaCppDescriptor = new(
            LlamaCppProviderRuntime.ProviderId,
            LlamaCppProviderRuntime.ProviderName);
        InferenceProviderRegistry providerRegistry = new(
        [
            new InferenceProviderRegistration(
                llamaCppDescriptor,
                _llamaCppRuntime,
                _llamaCppRuntime),
        ]);
        JsonInferenceProviderConfigurationStore providerConfigurationStore =
            _providerConfigurationStore ??= new JsonInferenceProviderConfigurationStore(
                providerConfigurationPath,
                LlamaCppProviderRuntime.ProviderId,
                legacyLlamaCppSettingsPath);
        StreamConversationTurnUseCase streamConversationTurn = new(
            runtime.Repository,
            providerRegistry,
            timeProvider);

        return new MainViewModel(
            runtime.CreateConversation,
            runtime.AppendMessage,
            streamConversationTurn,
            runtime.LoadConversation,
            runtime.ListConversations,
            runtime.RenameConversation,
            runtime.DeleteConversation,
            providerRegistry,
            providerConfigurationStore);
    }
}
