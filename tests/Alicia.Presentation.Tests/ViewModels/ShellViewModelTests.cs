using Alicia.Application.Conversations;
using Alicia.Application.Providers;
using Alicia.Presentation.ViewModels;

namespace Alicia.Presentation.Tests.ViewModels;

public sealed class ShellViewModelTests
{
    [Fact]
    public void ConstructorSelectsConversationsAndRetainsSharedWorkspace()
    {
        MainViewModel workspace = CreateWorkspace();

        ShellViewModel shell = new(workspace);

        Assert.Same(workspace, shell.Workspace);
        Assert.Equal(WorkspaceSection.Conversations, shell.SelectedSection);
        Assert.True(shell.IsConversationsSelected);
        Assert.False(shell.IsProvidersSelected);
        Assert.False(shell.IsModelsSelected);
    }

    [Fact]
    public void NavigationCommandsSwitchOnlyTheShellSection()
    {
        MainViewModel workspace = CreateWorkspace();
        ShellViewModel shell = new(workspace);

        shell.NavigateToProvidersCommand.Execute(null);

        Assert.Equal(WorkspaceSection.Providers, shell.SelectedSection);
        Assert.False(shell.IsConversationsSelected);
        Assert.True(shell.IsProvidersSelected);
        Assert.False(shell.IsModelsSelected);
        Assert.Same(workspace, shell.Workspace);

        shell.NavigateToModelsCommand.Execute(null);

        Assert.Equal(WorkspaceSection.Models, shell.SelectedSection);
        Assert.False(shell.IsConversationsSelected);
        Assert.False(shell.IsProvidersSelected);
        Assert.True(shell.IsModelsSelected);
        Assert.Same(workspace, shell.Workspace);

        shell.NavigateToConversationsCommand.Execute(null);

        Assert.Equal(WorkspaceSection.Conversations, shell.SelectedSection);
        Assert.True(shell.IsConversationsSelected);
        Assert.False(shell.IsProvidersSelected);
        Assert.False(shell.IsModelsSelected);
        Assert.Same(workspace, shell.Workspace);
    }

    [Fact]
    public async Task ConfigurationGateCanNavigateShellToModelsWorkspace()
    {
        StubInferenceProviderRuntime provider = new(InferenceProviderState.Ready);
        MainViewModel workspace = CreateWorkspace(
            provider,
            new StubInferenceProviderConfigurationStore());
        ShellViewModel shell = new(workspace);

        await workspace.InitializeAsync().ConfigureAwait(true);

        Assert.Equal(
            ConversationConfigurationGateState.ModelConfigurationRequired,
            workspace.ConfigurationGate.State);

        await workspace.ConfigurationGatePrimaryCommand.ExecuteAsync(null).ConfigureAwait(true);

        Assert.Equal(WorkspaceSection.Models, shell.SelectedSection);
        Assert.True(shell.IsModelsSelected);
    }

    private static MainViewModel CreateWorkspace(
        StubInferenceProviderRuntime? provider = null,
        StubInferenceProviderConfigurationStore? configurationStore = null)
    {
        InMemoryConversationRepository repository = new();
        TimeProvider timeProvider = new MutableTimeProvider(
            new DateTimeOffset(2026, 8, 12, 4, 0, 0, TimeSpan.Zero));
        StubInferenceProviderRuntime resolvedProvider = provider ?? new StubInferenceProviderRuntime();
        StubInferenceProviderRegistry providerRegistry = new(resolvedProvider);
        StubInferenceProviderConfigurationStore resolvedConfigurationStore = configurationStore
            ?? new StubInferenceProviderConfigurationStore(
                new InferenceProviderConfiguration(
                    "llama.cpp.cuda",
                    "owner/model-GGUF:Q4_K_M"));

        return new MainViewModel(
            new CreateConversationUseCase(repository, timeProvider),
            new AppendMessageUseCase(repository, timeProvider),
            new StreamConversationTurnUseCase(
                repository,
                new DeterministicConversationResponder("Development response"),
                timeProvider),
            new LoadConversationUseCase(repository),
            new ListConversationsUseCase(repository),
            new RenameConversationUseCase(repository, timeProvider),
            new DeleteConversationUseCase(repository),
            providerRegistry,
            resolvedConfigurationStore);
    }
}
