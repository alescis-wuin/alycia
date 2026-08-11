using System.Collections.ObjectModel;
using Alicia.Application.Conversations;
using Alicia.Domain.Conversations;
using CommunityToolkit.Mvvm.Input;

namespace Alicia.Presentation.ViewModels;

public sealed class MainViewModel : ViewModelBase
{
    private readonly AppendMessageUseCase _appendMessage;
    private readonly CompleteConversationTurnUseCase _completeConversationTurn;
    private readonly CreateConversationUseCase _createConversation;
    private readonly DeleteConversationUseCase _deleteConversation;
    private readonly ListConversationsUseCase _listConversations;
    private readonly LoadConversationUseCase _loadConversation;
    private readonly RenameConversationUseCase _renameConversation;

    private bool _isBusy;
    private bool _isDeleteConfirmationVisible;
    private bool _isGeneratingResponse;
    private bool _isInitialized;
    private bool _isSendingMessage;
    private ConversationId? _retryConversationId;
    private MessageId? _retryTriggeringMessageId;
    private CancellationTokenSource? _responseCancellation;
    private ConversationListItemViewModel? _selectedConversation;
    private string? _errorMessage;
    private string _messageDraft = string.Empty;

    public MainViewModel(
        CreateConversationUseCase createConversation,
        AppendMessageUseCase appendMessage,
        CompleteConversationTurnUseCase completeConversationTurn,
        LoadConversationUseCase loadConversation,
        ListConversationsUseCase listConversations,
        RenameConversationUseCase renameConversation,
        DeleteConversationUseCase deleteConversation)
    {
        ArgumentNullException.ThrowIfNull(createConversation);
        ArgumentNullException.ThrowIfNull(appendMessage);
        ArgumentNullException.ThrowIfNull(completeConversationTurn);
        ArgumentNullException.ThrowIfNull(loadConversation);
        ArgumentNullException.ThrowIfNull(listConversations);
        ArgumentNullException.ThrowIfNull(renameConversation);
        ArgumentNullException.ThrowIfNull(deleteConversation);

        _createConversation = createConversation;
        _appendMessage = appendMessage;
        _completeConversationTurn = completeConversationTurn;
        _loadConversation = loadConversation;
        _listConversations = listConversations;
        _renameConversation = renameConversation;
        _deleteConversation = deleteConversation;

        CreateConversationCommand = new AsyncRelayCommand(CreateConversationAsync);
        RefreshCommand = new AsyncRelayCommand(RefreshAsync);
        SendMessageCommand = new AsyncRelayCommand(SendMessageAsync, () => CanSendMessage);
        RetryResponseCommand = new AsyncRelayCommand(RetryResponseAsync, () => CanRetryResponse);
        StopResponseCommand = new RelayCommand(StopResponse, () => CanStopResponse);
        BeginRenameCommand = new RelayCommand(BeginRename);
        RequestDeleteCommand = new RelayCommand(RequestDelete);
        CancelDeleteCommand = new RelayCommand(CancelDelete);
        ConfirmDeleteCommand = new AsyncRelayCommand(ConfirmDeleteAsync);
        CancelTransientActionCommand = new RelayCommand(CancelTransientAction);
        DismissErrorCommand = new RelayCommand(ClearError);
    }

    public string ApplicationName { get; } = "Alicia";

    public ObservableCollection<ConversationListItemViewModel> Conversations { get; } = [];

    public ObservableCollection<MessageViewModel> Messages { get; } = [];

    public IAsyncRelayCommand CreateConversationCommand { get; }

    public IAsyncRelayCommand RefreshCommand { get; }

    public IAsyncRelayCommand SendMessageCommand { get; }

    public IAsyncRelayCommand RetryResponseCommand { get; }

    public IRelayCommand StopResponseCommand { get; }

    public IRelayCommand BeginRenameCommand { get; }

    public IRelayCommand RequestDeleteCommand { get; }

    public IRelayCommand CancelDeleteCommand { get; }

    public IAsyncRelayCommand ConfirmDeleteCommand { get; }

    public IRelayCommand CancelTransientActionCommand { get; }

    public IRelayCommand DismissErrorCommand { get; }

    public ConversationListItemViewModel? SelectedConversation
    {
        get => _selectedConversation;
        private set
        {
            if (_selectedConversation == value)
            {
                return;
            }

            bool conversationChanged = _selectedConversation?.Id != value?.Id;

            _selectedConversation?.SetSelected(false);
            _selectedConversation = value;
            _selectedConversation?.SetSelected(true);

            if (conversationChanged)
            {
                MessageDraft = string.Empty;
                ClearRetryResponse();
            }

            OnPropertyChanged();
            RaiseSelectionStateChanged();
        }
    }

    public string MessageDraft
    {
        get => _messageDraft;
        set
        {
            if (SetProperty(ref _messageDraft, value))
            {
                OnPropertyChanged(nameof(CanSendMessage));
                SendMessageCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                OnPropertyChanged(nameof(IsInteractionEnabled));
                OnPropertyChanged(nameof(CanSendMessage));
                OnPropertyChanged(nameof(IsComposerEnabled));
                OnPropertyChanged(nameof(SendButtonLabel));
                OnPropertyChanged(nameof(ComposerStatusText));
                OnPropertyChanged(nameof(StatusText));
                OnPropertyChanged(nameof(CanRetryResponse));
                SendMessageCommand.NotifyCanExecuteChanged();
                RetryResponseCommand.NotifyCanExecuteChanged();

                foreach (ConversationListItemViewModel item in Conversations)
                {
                    item.NotifyInteractionStateChanged();
                }
            }
        }
    }

    public bool IsInteractionEnabled => !IsBusy;

    public bool IsComposerEnabled => IsInteractionEnabled
        && HasSelectedConversation
        && !IsDeleteConfirmationVisible;

    public bool CanSendMessage => IsComposerEnabled && !string.IsNullOrWhiteSpace(MessageDraft);

    public bool IsSendingMessage
    {
        get => _isSendingMessage;
        private set
        {
            if (SetProperty(ref _isSendingMessage, value))
            {
                OnPropertyChanged(nameof(SendButtonLabel));
                OnPropertyChanged(nameof(ComposerStatusText));
                OnPropertyChanged(nameof(StatusText));
            }
        }
    }

    public bool IsGeneratingResponse
    {
        get => _isGeneratingResponse;
        private set
        {
            if (SetProperty(ref _isGeneratingResponse, value))
            {
                OnPropertyChanged(nameof(CanStopResponse));
                OnPropertyChanged(nameof(CanRetryResponse));
                OnPropertyChanged(nameof(ComposerStatusText));
                OnPropertyChanged(nameof(StatusText));
                StopResponseCommand.NotifyCanExecuteChanged();
                RetryResponseCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public bool CanStopResponse => IsGeneratingResponse
        && _responseCancellation is { IsCancellationRequested: false };

    public bool CanRetryResponse => !IsBusy
        && !IsGeneratingResponse
        && _retryConversationId is ConversationId retryConversationId
        && _retryTriggeringMessageId is not null
        && SelectedConversation?.Id == retryConversationId;

    public bool IsInitialized
    {
        get => _isInitialized;
        private set
        {
            if (SetProperty(ref _isInitialized, value))
            {
                RaiseEmptyStateChanged();
            }
        }
    }

    public bool IsDeleteConfirmationVisible
    {
        get => _isDeleteConfirmationVisible;
        private set
        {
            if (SetProperty(ref _isDeleteConfirmationVisible, value))
            {
                OnPropertyChanged(nameof(CanSendMessage));
                OnPropertyChanged(nameof(IsComposerEnabled));
                SendMessageCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public string? ErrorMessage
    {
        get => _errorMessage;
        private set
        {
            if (SetProperty(ref _errorMessage, value))
            {
                OnPropertyChanged(nameof(HasError));
            }
        }
    }

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    public bool HasConversationHistory => Conversations.Count > 0;

    public bool IsHistoryEmpty => IsInitialized && Conversations.Count == 0;

    public bool HasSelectedConversation => SelectedConversation is not null;

    public bool HasMessages => Messages.Count > 0;

    public bool IsSelectedConversationEmpty => IsInitialized && HasSelectedConversation && Messages.Count == 0;

    public bool ShowNoSelectionState => IsInitialized && HasConversationHistory && !HasSelectedConversation;

    public bool ShowLoadingState => !IsInitialized;

    public string SelectedConversationTitle => SelectedConversation?.Title ?? "No conversation selected";

    public string SelectedConversationMeta => SelectedConversation is null
        ? "Choose a conversation from history."
        : $"{Messages.Count} message{(Messages.Count == 1 ? string.Empty : "s")}";

    public string DeletePrompt => SelectedConversation is null
        ? "Delete this conversation?"
        : $"Delete ‘{SelectedConversation.Title}’?";

    public string ComposerPlaceholder => HasSelectedConversation
        ? "Write a local message…"
        : "Select a conversation to write a message";

    public string ComposerStatusText => IsSendingMessage
        ? "Saving message locally…"
        : IsGeneratingResponse
            ? "Alicia is responding… Stop is available."
            : CanRetryResponse
                ? "Response not completed • Retry response is available"
                : "Enter sends • Shift+Enter adds a new line • Local development responder";

    public string SendButtonLabel => IsSendingMessage ? "Saving…" : "Send";

    public string StatusText => IsSendingMessage
        ? "Saving message…"
        : IsGeneratingResponse
            ? "Alicia is responding…"
            : CanRetryResponse
                ? "Response incomplete"
                : IsBusy
                    ? "Working…"
                    : "Local history ready";

    public async Task InitializeAsync()
    {
        if (IsInitialized || IsBusy)
        {
            return;
        }

        await ExecuteOperationAsync(async () =>
        {
            await ReloadConversationsAsync(preferredConversationId: null).ConfigureAwait(true);
        }).ConfigureAwait(true);

        IsInitialized = true;
    }

    private async Task CreateConversationAsync()
    {
        await ExecuteOperationAsync(async () =>
        {
            Conversation conversation = await _createConversation
                .ExecuteAsync()
                .ConfigureAwait(true);

            await ReloadConversationsAsync(conversation.Id).ConfigureAwait(true);
        }).ConfigureAwait(true);
    }

    private async Task RefreshAsync()
    {
        ConversationId? selectedId = SelectedConversation?.Id;

        await ExecuteOperationAsync(async () =>
        {
            await ReloadConversationsAsync(selectedId).ConfigureAwait(true);
        }).ConfigureAwait(true);
    }

    private async Task SendMessageAsync()
    {
        if (!CanSendMessage || SelectedConversation is null)
        {
            return;
        }

        ConversationId conversationId = SelectedConversation.Id;
        string content = MessageDraft.Trim();

        await ExecuteOperationAsync(async () =>
        {
            CancelAllRenames();
            IsDeleteConfirmationVisible = false;
            IsSendingMessage = true;

            ChatMessage userMessage;

            try
            {
                userMessage = await _appendMessage
                    .ExecuteAsync(conversationId, MessageRole.User, content)
                    .ConfigureAwait(true);

                MessageDraft = string.Empty;
                await ReloadConversationsAsync(conversationId).ConfigureAwait(true);
            }
            finally
            {
                IsSendingMessage = false;
            }

            await GenerateResponseAsync(
                conversationId,
                userMessage.Id).ConfigureAwait(true);
        }).ConfigureAwait(true);
    }

    private async Task RetryResponseAsync()
    {
        if (!CanRetryResponse
            || _retryConversationId is not ConversationId conversationId
            || _retryTriggeringMessageId is not MessageId triggeringMessageId)
        {
            return;
        }

        await ExecuteOperationAsync(async () =>
        {
            await GenerateResponseAsync(
                conversationId,
                triggeringMessageId).ConfigureAwait(true);
        }).ConfigureAwait(true);
    }

    private async Task GenerateResponseAsync(
        ConversationId conversationId,
        MessageId triggeringMessageId)
    {
        SetRetryResponse(conversationId, triggeringMessageId);

        using CancellationTokenSource cancellationSource = new();
        _responseCancellation = cancellationSource;
        IsGeneratingResponse = true;

        try
        {
            await _completeConversationTurn
                .ExecuteAsync(
                    conversationId,
                    triggeringMessageId,
                    cancellationSource.Token)
                .ConfigureAwait(true);

            ClearRetryResponse();
            await ReloadConversationsAsync(conversationId).ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (cancellationSource.IsCancellationRequested)
        {
            ClearError();
        }
        finally
        {
            IsGeneratingResponse = false;

            if (ReferenceEquals(_responseCancellation, cancellationSource))
            {
                _responseCancellation = null;
            }

            OnPropertyChanged(nameof(CanStopResponse));
            StopResponseCommand.NotifyCanExecuteChanged();
        }
    }

    private void StopResponse()
    {
        if (!CanStopResponse)
        {
            return;
        }

        _responseCancellation?.Cancel();
        OnPropertyChanged(nameof(CanStopResponse));
        OnPropertyChanged(nameof(ComposerStatusText));
        OnPropertyChanged(nameof(StatusText));
        StopResponseCommand.NotifyCanExecuteChanged();
    }

    private void BeginRename()
    {
        if (SelectedConversation is null || IsBusy)
        {
            return;
        }

        CancelAllRenames();
        IsDeleteConfirmationVisible = false;
        SelectedConversation.BeginRename();
    }

    private void RequestDelete()
    {
        if (SelectedConversation is null || IsBusy)
        {
            return;
        }

        CancelAllRenames();
        IsDeleteConfirmationVisible = true;
    }

    private void CancelDelete()
    {
        IsDeleteConfirmationVisible = false;
    }

    private void CancelTransientAction()
    {
        if (CanStopResponse)
        {
            StopResponse();
            return;
        }

        CancelAllRenames();
        CancelDelete();
    }

    private async Task ConfirmDeleteAsync()
    {
        if (SelectedConversation is null)
        {
            return;
        }

        ConversationId conversationId = SelectedConversation.Id;

        await ExecuteOperationAsync(async () =>
        {
            await _deleteConversation
                .ExecuteAsync(conversationId)
                .ConfigureAwait(true);

            IsDeleteConfirmationVisible = false;
            ClearSelection();
            await ReloadConversationsAsync(preferredConversationId: null).ConfigureAwait(true);
        }).ConfigureAwait(true);
    }

    private async Task SelectConversationAsync(ConversationListItemViewModel conversation)
    {
        ArgumentNullException.ThrowIfNull(conversation);

        if (IsBusy || SelectedConversation?.Id == conversation.Id)
        {
            return;
        }

        await ExecuteOperationAsync(async () =>
        {
            await LoadConversationAsync(conversation).ConfigureAwait(true);
        }).ConfigureAwait(true);
    }

    private async Task BeginRenameConversationAsync(ConversationListItemViewModel conversation)
    {
        ArgumentNullException.ThrowIfNull(conversation);

        if (!await EnsureSelectedConversationAsync(conversation).ConfigureAwait(true))
        {
            return;
        }

        CancelAllRenames();
        IsDeleteConfirmationVisible = false;
        conversation.BeginRename();
    }

    private async Task SaveRenameConversationAsync(ConversationListItemViewModel conversation)
    {
        ArgumentNullException.ThrowIfNull(conversation);

        if (IsBusy
            || SelectedConversation?.Id != conversation.Id
            || !conversation.CanSaveRename)
        {
            return;
        }

        ConversationId conversationId = conversation.Id;
        string title = conversation.RenameTitle;

        await ExecuteOperationAsync(async () =>
        {
            await _renameConversation
                .ExecuteAsync(conversationId, title)
                .ConfigureAwait(true);

            await ReloadConversationsAsync(conversationId).ConfigureAwait(true);
        }).ConfigureAwait(true);
    }

    private async Task RequestDeleteConversationAsync(ConversationListItemViewModel conversation)
    {
        ArgumentNullException.ThrowIfNull(conversation);

        if (!await EnsureSelectedConversationAsync(conversation).ConfigureAwait(true))
        {
            return;
        }

        RequestDelete();
    }

    private async Task<bool> EnsureSelectedConversationAsync(ConversationListItemViewModel conversation)
    {
        if (IsBusy)
        {
            return false;
        }

        if (SelectedConversation?.Id != conversation.Id)
        {
            await ExecuteOperationAsync(async () =>
            {
                await LoadConversationAsync(conversation).ConfigureAwait(true);
            }).ConfigureAwait(true);
        }

        return !HasError && SelectedConversation?.Id == conversation.Id;
    }

    private async Task ReloadConversationsAsync(ConversationId? preferredConversationId)
    {
        IReadOnlyList<ConversationSummary> summaries = await _listConversations
            .ExecuteAsync()
            .ConfigureAwait(true);

        Conversations.Clear();

        foreach (ConversationSummary summary in summaries)
        {
            Conversations.Add(new ConversationListItemViewModel(
                summary,
                SelectConversationAsync,
                BeginRenameConversationAsync,
                SaveRenameConversationAsync,
                RequestDeleteConversationAsync,
                () => IsInteractionEnabled));
        }

        RaiseHistoryStateChanged();

        if (Conversations.Count == 0)
        {
            ClearSelection();
            return;
        }

        ConversationListItemViewModel target = preferredConversationId is ConversationId preferredId
            ? Conversations.FirstOrDefault(item => item.Id == preferredId) ?? Conversations[0]
            : Conversations[0];

        await LoadConversationAsync(target).ConfigureAwait(true);
    }

    private async Task LoadConversationAsync(ConversationListItemViewModel item)
    {
        Conversation conversation = await _loadConversation
            .ExecuteAsync(item.Id)
            .ConfigureAwait(true);

        CancelAllRenames();
        SelectedConversation = item;
        Messages.Clear();

        foreach (ChatMessage message in conversation.Messages)
        {
            Messages.Add(new MessageViewModel(message));
        }

        IsDeleteConfirmationVisible = false;
        RaiseMessageStateChanged();
    }

    private void ClearSelection()
    {
        CancelAllRenames();
        SelectedConversation = null;
        Messages.Clear();
        IsDeleteConfirmationVisible = false;
        RaiseMessageStateChanged();
    }

    private void CancelAllRenames()
    {
        foreach (ConversationListItemViewModel item in Conversations)
        {
            item.CancelRename();
        }
    }

    private async Task ExecuteOperationAsync(Func<Task> operation)
    {
        ArgumentNullException.ThrowIfNull(operation);

        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        ClearError();

        try
        {
            await operation().ConfigureAwait(true);
        }
        catch (InvalidDataException exception)
        {
            ErrorMessage = exception.Message;
        }
        catch (IOException exception)
        {
            ErrorMessage = exception.Message;
        }
        catch (UnauthorizedAccessException exception)
        {
            ErrorMessage = exception.Message;
        }
        catch (KeyNotFoundException exception)
        {
            ErrorMessage = exception.Message;
        }
        catch (ArgumentException exception)
        {
            ErrorMessage = exception.Message;
        }
        catch (InvalidOperationException exception)
        {
            ErrorMessage = exception.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void SetRetryResponse(
        ConversationId conversationId,
        MessageId triggeringMessageId)
    {
        _retryConversationId = conversationId;
        _retryTriggeringMessageId = triggeringMessageId;
        OnPropertyChanged(nameof(CanRetryResponse));
        OnPropertyChanged(nameof(ComposerStatusText));
        OnPropertyChanged(nameof(StatusText));
        RetryResponseCommand.NotifyCanExecuteChanged();
    }

    private void ClearRetryResponse()
    {
        if (_retryConversationId is null && _retryTriggeringMessageId is null)
        {
            return;
        }

        _retryConversationId = null;
        _retryTriggeringMessageId = null;
        OnPropertyChanged(nameof(CanRetryResponse));
        OnPropertyChanged(nameof(ComposerStatusText));
        OnPropertyChanged(nameof(StatusText));
        RetryResponseCommand.NotifyCanExecuteChanged();
    }

    private void ClearError()
    {
        ErrorMessage = null;
    }

    private void RaiseHistoryStateChanged()
    {
        OnPropertyChanged(nameof(HasConversationHistory));
        RaiseEmptyStateChanged();
    }

    private void RaiseSelectionStateChanged()
    {
        OnPropertyChanged(nameof(HasSelectedConversation));
        OnPropertyChanged(nameof(SelectedConversationTitle));
        OnPropertyChanged(nameof(DeletePrompt));
        OnPropertyChanged(nameof(CanSendMessage));
        OnPropertyChanged(nameof(IsComposerEnabled));
        OnPropertyChanged(nameof(ComposerPlaceholder));
        OnPropertyChanged(nameof(CanRetryResponse));
        SendMessageCommand.NotifyCanExecuteChanged();
        RetryResponseCommand.NotifyCanExecuteChanged();
        RaiseMessageStateChanged();
        RaiseEmptyStateChanged();
    }

    private void RaiseMessageStateChanged()
    {
        OnPropertyChanged(nameof(HasMessages));
        OnPropertyChanged(nameof(IsSelectedConversationEmpty));
        OnPropertyChanged(nameof(SelectedConversationMeta));
    }

    private void RaiseEmptyStateChanged()
    {
        OnPropertyChanged(nameof(IsHistoryEmpty));
        OnPropertyChanged(nameof(IsSelectedConversationEmpty));
        OnPropertyChanged(nameof(ShowNoSelectionState));
        OnPropertyChanged(nameof(ShowLoadingState));
    }
}
