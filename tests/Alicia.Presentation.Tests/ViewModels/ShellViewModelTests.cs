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
        Assert.False(shell.IsProfilesSelected);
        Assert.Equal("Current workspace", shell.ConversationsNavigationStatus);
        Assert.Equal("Available workspace", shell.ProvidersNavigationStatus);
        Assert.Equal("Available workspace", shell.ModelsNavigationStatus);
        Assert.Equal("Available workspace", shell.ProfilesNavigationStatus);
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
        Assert.False(shell.IsProfilesSelected);
        Assert.Equal("Available workspace", shell.ConversationsNavigationStatus);
        Assert.Equal("Current workspace", shell.ProvidersNavigationStatus);
        Assert.Equal("Available workspace", shell.ModelsNavigationStatus);
        Assert.Equal("Available workspace", shell.ProfilesNavigationStatus);
        Assert.Same(workspace, shell.Workspace);

        shell.NavigateToModelsCommand.Execute(null);

        Assert.Equal(WorkspaceSection.Models, shell.SelectedSection);
        Assert.False(shell.IsConversationsSelected);
        Assert.False(shell.IsProvidersSelected);
        Assert.True(shell.IsModelsSelected);
        Assert.False(shell.IsProfilesSelected);
        Assert.Same(workspace, shell.Workspace);

        shell.NavigateToProfilesCommand.Execute(null);

        Assert.Equal(WorkspaceSection.Profiles, shell.SelectedSection);
        Assert.False(shell.IsConversationsSelected);
        Assert.False(shell.IsProvidersSelected);
        Assert.False(shell.IsModelsSelected);
        Assert.True(shell.IsProfilesSelected);
        Assert.Equal("Current workspace", shell.ProfilesNavigationStatus);
        Assert.Same(workspace, shell.Workspace);

        shell.NavigateToConversationsCommand.Execute(null);

        Assert.Equal(WorkspaceSection.Conversations, shell.SelectedSection);
        Assert.True(shell.IsConversationsSelected);
        Assert.False(shell.IsProvidersSelected);
        Assert.False(shell.IsModelsSelected);
        Assert.False(shell.IsProfilesSelected);
        Assert.Same(workspace, shell.Workspace);
    }

    [Fact]
    public void ReducedMotionPreferenceIsProjectedThroughShell()
    {
        MainViewModel workspace = CreateWorkspace(isReducedMotionEnabled: true);
        ShellViewModel shell = new(workspace);

        Assert.True(shell.IsReducedMotionEnabled);
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
        Assert.False(shell.IsProfilesSelected);
    }

    private static MainViewModel CreateWorkspace(
        StubInferenceProviderRuntime? provider = null,
        StubInferenceProviderConfigurationStore? configurationStore = null,
        bool isReducedMotionEnabled = false)
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
            resolvedConfigurationStore,
            isReducedMotionEnabled: isReducedMotionEnabled);
    }
}
