using Alicia.Application.Conversations;
using Alicia.Application.Providers;
using Alicia.Presentation.Tests.ViewModels;
using Alicia.Presentation.ViewModels;

namespace Alicia.Presentation.Tests.Ui;

internal static class UiTestMainViewModelFactory
{
    private static readonly DateTimeOffset _now = new(
        2026,
        9,
        24,
        6,
        0,
        0,
        TimeSpan.Zero);

    internal static MainViewModel Create(
        InferenceProviderState providerState = InferenceProviderState.Ready,
        InferenceModelLibrary? library = null,
        InferenceProviderConfiguration? configuration = null)
    {
        InMemoryConversationRepository repository = new();
        MutableTimeProvider timeProvider = new(_now);
        StubInferenceProviderRuntime providerRuntime = new(providerState);
        StubInferenceProviderRegistry providerRegistry = new(providerRuntime);
        InferenceProviderConfiguration resolvedConfiguration = configuration
            ?? new InferenceProviderConfiguration(
                "llama.cpp.cuda",
                "owner/model-GGUF:Q4_K_M",
                contextSize: 2048);
        StubInferenceProviderConfigurationStore configurationStore = new(resolvedConfiguration);
        InMemoryInferenceModelLibraryStore libraryStore = new(
            library ?? InferenceModelLibrary.Empty);

        return new MainViewModel(
            new CreateConversationUseCase(repository, timeProvider),
            new AppendMessageUseCase(repository, timeProvider),
            new StreamConversationTurnUseCase(
                repository,
                new DeterministicConversationResponder("Headless UI response"),
                timeProvider),
            new LoadConversationUseCase(repository),
            new ListConversationsUseCase(repository),
            new RenameConversationUseCase(repository, timeProvider),
            new DeleteConversationUseCase(repository),
            providerRegistry,
            configurationStore,
            TimeSpan.Zero,
            TimeSpan.Zero,
            conversationUiStateStore: null,
            isReducedMotionEnabled: true,
            generationProfileCatalogStore: null,
            conversationGenerationSelectionStore: null,
            generationProfileTimeProvider: timeProvider,
            generationProfileDraftAutosaveDelay: TimeSpan.Zero,
            inferenceModelLibraryStore: libraryStore);
    }

    internal static InferenceModelLibrary CreateLibrary(params InferenceProviderConfiguration[] configurations)
    {
        ArgumentNullException.ThrowIfNull(configurations);

        InferenceModelLibraryEntry[] entries = configurations
            .Select((configuration, index) => new InferenceModelLibraryEntry(
                configuration,
                _now.AddMinutes(-(index + 1))))
            .ToArray();

        return new InferenceModelLibrary(entries);
    }
}
