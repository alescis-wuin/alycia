using Alicia.Application.Conversations;
using Alicia.Application.Providers;
using Alicia.Desktop.Accessibility;
using Alicia.Infrastructure.Conversations;
using Alicia.Infrastructure.Providers;
using Alicia.Infrastructure.Providers.LlamaCpp;
using Alicia.Presentation;
using Alicia.Presentation.State;
using Alicia.Presentation.ViewModels;
using Avalonia;

namespace Alicia.Desktop;

internal static class Program
{
    private static LlamaCppProviderRuntime? _llamaCppRuntime;
    private static JsonInferenceProviderConfigurationStore? _providerConfigurationStore;
    private static JsonConversationUiStateStore? _conversationUiStateStore;
    private static MainViewModel? _mainViewModel;

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
            _mainViewModel?.PersistConversationUiStateAsync().GetAwaiter().GetResult();

            if (_llamaCppRuntime is not null)
            {
                _llamaCppRuntime.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }

            _providerConfigurationStore?.Dispose();
            _conversationUiStateStore?.Dispose();
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
        if (_mainViewModel is not null)
        {
            return _mainViewModel;
        }

        string localApplicationData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string applicationDirectory = Path.Combine(localApplicationData, "Alicia");
        string storageDirectory = Path.Combine(applicationDirectory, "conversations");
        string providersDirectory = Path.Combine(applicationDirectory, "providers");
        string uiStatePath = Path.Combine(applicationDirectory, "ui-state.json");
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
        JsonConversationUiStateStore conversationUiStateStore =
            _conversationUiStateStore ??= new JsonConversationUiStateStore(uiStatePath);

        _mainViewModel = new MainViewModel(
            runtime.CreateConversation,
            runtime.AppendMessage,
            streamConversationTurn,
            runtime.LoadConversation,
            runtime.ListConversations,
            runtime.RenameConversation,
            runtime.DeleteConversation,
            providerRegistry,
            providerConfigurationStore,
            conversationUiStateStore: conversationUiStateStore,
            isReducedMotionEnabled: ReducedMotionPreference.IsEnabled());
        return _mainViewModel;
    }
}
