namespace Alicia.Presentation.ViewModels;

public sealed class ConversationConfigurationGateViewModel : ViewModelBase
{
    private ConversationConfigurationGateAction _primaryAction;
    private string _primaryActionLabel = string.Empty;
    private string _description = string.Empty;
    private bool _isPrimaryActionEnabled;
    private string _title = string.Empty;
    private ConversationConfigurationGateState _state = ConversationConfigurationGateState.Hidden;

    public ConversationConfigurationGateState State
    {
        get => _state;
        private set
        {
            if (SetProperty(ref _state, value))
            {
                OnPropertyChanged(nameof(IsVisible));
            }
        }
    }

    public bool IsVisible => State != ConversationConfigurationGateState.Hidden;

    public string Title
    {
        get => _title;
        private set => SetProperty(ref _title, value);
    }

    public string Description
    {
        get => _description;
        private set => SetProperty(ref _description, value);
    }

    public string PrimaryActionLabel
    {
        get => _primaryActionLabel;
        private set => SetProperty(ref _primaryActionLabel, value);
    }

    public bool IsPrimaryActionEnabled
    {
        get => _isPrimaryActionEnabled;
        private set => SetProperty(ref _isPrimaryActionEnabled, value);
    }

    internal ConversationConfigurationGateAction PrimaryAction => _primaryAction;

    internal void Hide()
    {
        State = ConversationConfigurationGateState.Hidden;
        Title = string.Empty;
        Description = string.Empty;
        _primaryAction = ConversationConfigurationGateAction.None;
        PrimaryActionLabel = string.Empty;
        IsPrimaryActionEnabled = false;
    }

    internal void Configure(
        ConversationConfigurationGateState state,
        string title,
        string description,
        ConversationConfigurationGateAction primaryAction,
        string primaryActionLabel,
        bool isPrimaryActionEnabled)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        ArgumentException.ThrowIfNullOrWhiteSpace(primaryActionLabel);

        State = state;
        Title = title;
        Description = description;
        _primaryAction = primaryAction;
        PrimaryActionLabel = primaryActionLabel;
        IsPrimaryActionEnabled = isPrimaryActionEnabled;
    }
}

internal enum ConversationConfigurationGateAction
{
    None,
    OpenProviders,
    DetectProvider,
    InstallProvider,
    OpenModels,
    StartProvider,
}
