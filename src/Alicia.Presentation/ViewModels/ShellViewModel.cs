using CommunityToolkit.Mvvm.Input;

namespace Alicia.Presentation.ViewModels;

public sealed class ShellViewModel : ViewModelBase
{
    private WorkspaceSection _selectedSection = WorkspaceSection.Conversations;

    public ShellViewModel(MainViewModel workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);

        Workspace = workspace;
        Workspace.WorkspaceNavigationRequested += NavigateToRequestedSection;
        NavigateToConversationsCommand = new RelayCommand(
            () => SelectedSection = WorkspaceSection.Conversations);
        NavigateToProvidersCommand = new RelayCommand(
            () => SelectedSection = WorkspaceSection.Providers);
        NavigateToModelsCommand = new RelayCommand(
            () => SelectedSection = WorkspaceSection.Models);
    }

    public MainViewModel Workspace { get; }

    public IRelayCommand NavigateToConversationsCommand { get; }

    public IRelayCommand NavigateToProvidersCommand { get; }

    public IRelayCommand NavigateToModelsCommand { get; }

    public WorkspaceSection SelectedSection
    {
        get => _selectedSection;
        private set
        {
            if (SetProperty(ref _selectedSection, value))
            {
                OnPropertyChanged(nameof(IsConversationsSelected));
                OnPropertyChanged(nameof(IsProvidersSelected));
                OnPropertyChanged(nameof(IsModelsSelected));
                OnPropertyChanged(nameof(ConversationsNavigationStatus));
                OnPropertyChanged(nameof(ProvidersNavigationStatus));
                OnPropertyChanged(nameof(ModelsNavigationStatus));
            }
        }
    }

    public bool IsConversationsSelected => SelectedSection == WorkspaceSection.Conversations;

    public bool IsProvidersSelected => SelectedSection == WorkspaceSection.Providers;

    public bool IsModelsSelected => SelectedSection == WorkspaceSection.Models;

    public bool IsReducedMotionEnabled => Workspace.IsReducedMotionEnabled;

    public string ConversationsNavigationStatus => IsConversationsSelected
        ? "Current workspace"
        : "Available workspace";

    public string ProvidersNavigationStatus => IsProvidersSelected
        ? "Current workspace"
        : "Available workspace";

    public string ModelsNavigationStatus => IsModelsSelected
        ? "Current workspace"
        : "Available workspace";

    private void NavigateToRequestedSection(
        object? sender,
        WorkspaceNavigationRequestedEventArgs arguments)
    {
        SelectedSection = arguments.Section;
    }
}
