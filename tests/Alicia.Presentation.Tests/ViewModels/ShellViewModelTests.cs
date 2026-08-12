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

    private static MainViewModel CreateWorkspace()
    {
        InMemoryConversationRepository repository = new();
        TimeProvider timeProvider = new MutableTimeProvider(
            new DateTimeOffset(2026, 8, 12, 4, 0, 0, TimeSpan.Zero));
        StubInferenceProviderRuntime provider = new();
        StubInferenceProviderRegistry providerRegistry = new(provider);
        StubInferenceProviderConfigurationStore configurationStore = new(
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
            configurationStore);
    }
}
