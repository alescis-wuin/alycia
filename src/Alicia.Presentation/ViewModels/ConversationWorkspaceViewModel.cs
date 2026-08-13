namespace Alicia.Presentation.ViewModels;

public sealed class ConversationWorkspaceViewModel : ViewModelBase
{
    private bool _isBusy;
    private bool _isDeleteConfirmationVisible;
    private bool _isInitialized;
    private string? _errorMessage;
    private string _messageDraft = string.Empty;
    private ConversationListItemViewModel? _selectedConversation;

    public bool IsBusy
    {
        get => _isBusy;
        internal set => SetProperty(ref _isBusy, value);
    }

    public bool IsDeleteConfirmationVisible
    {
        get => _isDeleteConfirmationVisible;
        internal set => SetProperty(ref _isDeleteConfirmationVisible, value);
    }

    public bool IsInitialized
    {
        get => _isInitialized;
        internal set => SetProperty(ref _isInitialized, value);
    }

    public string? ErrorMessage
    {
        get => _errorMessage;
        internal set
        {
            if (SetProperty(ref _errorMessage, value))
            {
                OnPropertyChanged(nameof(HasError));
            }
        }
    }

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    public string MessageDraft
    {
        get => _messageDraft;
        internal set => SetProperty(ref _messageDraft, value);
    }

    public ConversationListItemViewModel? SelectedConversation => _selectedConversation;

    internal bool SetSelectedConversation(
        ConversationListItemViewModel? value,
        out bool conversationChanged)
    {
        if (_selectedConversation == value)
        {
            conversationChanged = false;
            return false;
        }

        conversationChanged = _selectedConversation?.Id != value?.Id;

        _selectedConversation?.SetSelected(false);
        _selectedConversation = value;
        _selectedConversation?.SetSelected(true);
        OnPropertyChanged(nameof(SelectedConversation));
        return true;
    }
}
