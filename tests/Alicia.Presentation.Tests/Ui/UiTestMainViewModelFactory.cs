using Alicia.Application.Conversations;
using Alicia.Application.Generations;
using Alicia.Application.Providers;
using Alicia.Domain.Conversations;
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

    internal static MainViewModel CreateComposerScenario(bool branchMismatch = false)
    {
        InMemoryConversationRepository repository = new();
        Conversation conversation = new(
            ConversationId.New(),
            "Headless composer",
            _now,
            _now);
        repository.Seed(conversation);

        InferenceProviderConfiguration currentConfiguration = new(
            "llama.cpp.cuda",
            "owner/model-GGUF:Q4_K_M",
            contextSize: 2048);
        InferenceProviderConfiguration branchConfiguration = branchMismatch
            ? new InferenceProviderConfiguration(
                "llama.cpp.cuda",
                "owner/branch-model-GGUF:Q5_K_M",
                contextSize: 4096)
            : currentConfiguration;

        GenerationProfileModelScope branchScope = new(
            branchConfiguration.ProviderId,
            branchConfiguration.ModelReference!);
        GenerationProfileCatalog branchCatalog = GenerationProfileCatalog.CreateEmpty(
            branchScope,
            GenerationProfileId.New());
        InMemoryGenerationProfileCatalogStore profileStore = new();
        profileStore.Seed(branchCatalog);

        InMemoryConversationGenerationSelectionStore selectionStore = new();
        selectionStore.Seed(new ConversationGenerationSelection(
            conversation.Id,
            conversation.ActiveBranchId,
            branchScope,
            branchCatalog.DefaultProfile.Id));

        InferenceModelLibrary library = branchMismatch
            ? new InferenceModelLibrary(
            [
                new InferenceModelLibraryEntry(currentConfiguration, _now.AddMinutes(-2)),
                new InferenceModelLibraryEntry(branchConfiguration, _now.AddMinutes(-1)),
            ])
            : new InferenceModelLibrary(
            [
                new InferenceModelLibraryEntry(currentConfiguration, _now.AddMinutes(-1)),
            ]);

        return CreateScenarioViewModel(
            repository,
            new StubInferenceProviderRuntime(InferenceProviderState.Running),
            new StubInferenceProviderConfigurationStore(currentConfiguration),
            profileStore,
            selectionStore,
            new InMemoryInferenceModelLibraryStore(library));
    }

    internal static MainViewModel CreateConfigurationGateScenario()
    {
        InMemoryConversationRepository repository = new();
        repository.Seed(new Conversation(
            ConversationId.New(),
            "Headless configuration gate",
            _now,
            _now));

        return CreateScenarioViewModel(
            repository,
            new StubInferenceProviderRuntime(InferenceProviderState.Ready),
            new StubInferenceProviderConfigurationStore(),
            null,
            null,
            new InMemoryInferenceModelLibraryStore(InferenceModelLibrary.Empty));
    }

    private static MainViewModel CreateScenarioViewModel(
        InMemoryConversationRepository repository,
        StubInferenceProviderRuntime providerRuntime,
        StubInferenceProviderConfigurationStore configurationStore,
        InMemoryGenerationProfileCatalogStore? generationProfileCatalogStore,
        InMemoryConversationGenerationSelectionStore? conversationGenerationSelectionStore,
        InMemoryInferenceModelLibraryStore libraryStore)
    {
        MutableTimeProvider timeProvider = new(_now);

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
            new StubInferenceProviderRegistry(providerRuntime),
            configurationStore,
            TimeSpan.Zero,
            TimeSpan.Zero,
            conversationUiStateStore: null,
            isReducedMotionEnabled: true,
            generationProfileCatalogStore: generationProfileCatalogStore,
            conversationGenerationSelectionStore: conversationGenerationSelectionStore,
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
