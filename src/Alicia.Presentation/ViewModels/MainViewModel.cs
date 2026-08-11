using System.Collections.ObjectModel;
using Alicia.Application.Conversations;
using Alicia.Application.Providers;
using Alicia.Domain.Conversations;
using CommunityToolkit.Mvvm.Input;

namespace Alicia.Presentation.ViewModels;

public sealed class MainViewModel : ViewModelBase
{
    private readonly AppendMessageUseCase _appendMessage;
    private readonly StreamConversationTurnUseCase _streamConversationTurn;
    private readonly CreateConversationUseCase _createConversation;
    private readonly DeleteConversationUseCase _deleteConversation;
    private readonly ListConversationsUseCase _listConversations;
    private readonly LoadConversationUseCase _loadConversation;
    private readonly RenameConversationUseCase _renameConversation;
    private readonly IInferenceProviderRuntime _inferenceProvider;

    private bool _isBusy;
    private bool _isDeleteConfirmationVisible;
    private bool _isGeneratingResponse;
    private bool _isInitialized;
    private bool _isSendingMessage;
    private bool _isProviderBusy;
    private ConversationId? _retryConversationId;
    private MessageId? _retryTriggeringMessageId;
    private CancellationTokenSource? _responseCancellation;
    private CancellationTokenSource? _providerOperationCancellation;
    private ConversationListItemViewModel? _selectedConversation;
    private string? _errorMessage;
    private string _messageDraft = string.Empty;
    private string _providerModelReference = string.Empty;
    private InferenceProviderProgress? _providerProgress;
    private InferenceProviderSnapshot? _providerSnapshot;

    public MainViewModel(
        CreateConversationUseCase createConversation,
        AppendMessageUseCase appendMessage,
        StreamConversationTurnUseCase streamConversationTurn,
        LoadConversationUseCase loadConversation,
        ListConversationsUseCase listConversations,
        RenameConversationUseCase renameConversation,
        DeleteConversationUseCase deleteConversation,
        IInferenceProviderRuntime inferenceProvider)
    {
        ArgumentNullException.ThrowIfNull(createConversation);
        ArgumentNullException.ThrowIfNull(appendMessage);
        ArgumentNullException.ThrowIfNull(streamConversationTurn);
        ArgumentNullException.ThrowIfNull(loadConversation);
        ArgumentNullException.ThrowIfNull(listConversations);
        ArgumentNullException.ThrowIfNull(renameConversation);
        ArgumentNullException.ThrowIfNull(deleteConversation);
        ArgumentNullException.ThrowIfNull(inferenceProvider);

        _createConversation = createConversation;
        _appendMessage = appendMessage;
        _streamConversationTurn = streamConversationTurn;
        _loadConversation = loadConversation;
        _listConversations = listConversations;
        _renameConversation = renameConversation;
        _deleteConversation = deleteConversation;
        _inferenceProvider = inferenceProvider;

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
        DetectProviderCommand = new AsyncRelayCommand(DetectProviderAsync, () => CanDetectProvider);
        InstallProviderCommand = new AsyncRelayCommand(InstallProviderAsync, () => CanInstallProvider);
        StartProviderCommand = new AsyncRelayCommand(StartProviderAsync, () => CanStartProvider);
        StopProviderCommand = new AsyncRelayCommand(StopProviderAsync, () => CanStopProvider);
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

    public IAsyncRelayCommand DetectProviderCommand { get; }

    public IAsyncRelayCommand InstallProviderCommand { get; }

    public IAsyncRelayCommand StartProviderCommand { get; }

    public IAsyncRelayCommand StopProviderCommand { get; }

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

    public string ProviderModelReference
    {
        get => _providerModelReference;
        set
        {
            if (SetProperty(ref _providerModelReference, value))
            {
                OnPropertyChanged(nameof(CanStartProvider));
                StartProviderCommand.NotifyCanExecuteChanged();
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

    public bool CanSendMessage => IsComposerEnabled
        && IsProviderRunning
        && !string.IsNullOrWhiteSpace(MessageDraft);

    public bool IsProviderBusy
    {
        get => _isProviderBusy;
        private set
        {
            if (SetProperty(ref _isProviderBusy, value))
            {
                RaiseProviderStateChanged();
            }
        }
    }

    public bool IsProviderRunning => _providerSnapshot?.State == InferenceProviderState.Running;

    public bool CanDetectProvider => !IsProviderBusy && !IsGeneratingResponse;

    public bool CanInstallProvider => !IsProviderBusy
        && !IsGeneratingResponse
        && _providerSnapshot?.State is InferenceProviderState.Missing
            or InferenceProviderState.Unsupported
            or InferenceProviderState.Faulted;

    public bool CanStartProvider => !IsProviderBusy
        && !IsGeneratingResponse
        && _providerSnapshot?.State == InferenceProviderState.Ready
        && !string.IsNullOrWhiteSpace(ProviderModelReference);

    public bool CanStopProvider => IsProviderRunning
        || _providerSnapshot?.State == InferenceProviderState.Starting;

    public bool IsProviderModelEditable => !IsProviderBusy && !IsProviderRunning;

    public string ProviderName => _providerSnapshot?.Name ?? "llama.cpp CUDA";

    public string ProviderStatusText => _providerSnapshot?.State switch
    {
        InferenceProviderState.Detecting => "Detecting",
        InferenceProviderState.Missing => "Not installed",
        InferenceProviderState.Ready => "Ready",
        InferenceProviderState.Installing => "Installing",
        InferenceProviderState.Starting => "Starting",
        InferenceProviderState.Running => "Running",
        InferenceProviderState.Stopping => "Stopping",
        InferenceProviderState.Unsupported => "CUDA unavailable",
        InferenceProviderState.Faulted => "Needs attention",
        _ => "Detecting",
    };

    public string ProviderDetailText => _providerSnapshot?.Detail
        ?? "Detecting the local inference provider…";

    public string ProviderVersionText => string.IsNullOrWhiteSpace(_providerSnapshot?.Version)
        ? "Version not detected"
        : $"Version {_providerSnapshot.Version}";

    public bool IsProviderProgressVisible => _providerProgress is not null;

    public bool IsProviderProgressIndeterminate => IsProviderBusy
        && _providerProgress?.Fraction is null;

    public double ProviderProgressValue => (_providerProgress?.Fraction ?? 0) * 100;

    public string ProviderProgressText => _providerProgress is null
        ? string.Empty
        : _providerProgress.Fraction is double fraction
            ? $"{_providerProgress.Stage} • {fraction:P0}"
            : _providerProgress.Stage;

    public string ProviderProgressDetailText => _providerProgress?.Detail ?? string.Empty;

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
                RaiseProviderStateChanged();
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

    public string ComposerPlaceholder => !IsProviderRunning
        ? "Start llama.cpp CUDA to enable local AI chat"
        : HasSelectedConversation
            ? "Write a local message…"
            : "Select a conversation to write a message";

    public string ComposerStatusText => IsSendingMessage
        ? "Saving message locally…"
        : IsGeneratingResponse
            ? "Alicia is streaming a response… Stop is available."
            : CanRetryResponse
                ? "Response not completed • Retry response is available"
                : !IsProviderRunning
                    ? "Start llama.cpp CUDA before sending messages"
                    : $"Enter sends • Shift+Enter adds a new line • {ProviderModelReference}";

    public string SendButtonLabel => IsSendingMessage ? "Saving…" : "Send";

    public string StatusText => IsSendingMessage
        ? "Saving message…"
        : IsGeneratingResponse
            ? "Alicia is streaming a response…"
            : CanRetryResponse
                ? "Response incomplete"
                : IsBusy
                    ? "Working…"
                    : IsProviderRunning
                        ? "Local AI ready"
                        : "Local history ready • AI provider stopped";

    public async Task InitializeAsync()
    {
        if (IsInitialized || IsBusy)
        {
            return;
        }

        await DetectProviderAsync().ConfigureAwait(true);

        await ExecuteOperationAsync(async () =>
        {
            await ReloadConversationsAsync(preferredConversationId: null).ConfigureAwait(true);
        }).ConfigureAwait(true);

        IsInitialized = true;
    }

    private async Task DetectProviderAsync()
    {
        ClearProviderProgress();

        await ExecuteProviderOperationAsync(
            InferenceProviderState.Detecting,
            "Detecting llama.cpp and CUDA support…",
            async cancellationToken => await _inferenceProvider
                .DetectAsync(cancellationToken)
                .ConfigureAwait(true)).ConfigureAwait(true);
    }

    private async Task InstallProviderAsync()
    {
        if (!CanInstallProvider)
        {
            return;
        }

        ClearProviderProgress();
        Progress<InferenceProviderProgress> progress = new(ApplyProviderProgress);

        await ExecuteProviderOperationAsync(
            InferenceProviderState.Installing,
            "Preparing the managed llama.cpp CUDA installation…",
            async cancellationToken => await _inferenceProvider
                .InstallAsync(progress, cancellationToken)
                .ConfigureAwait(true)).ConfigureAwait(true);
    }

    private async Task StartProviderAsync()
    {
        if (!CanStartProvider)
        {
            return;
        }

        string modelReference = ProviderModelReference.Trim();
        ClearProviderProgress();

        await ExecuteProviderOperationAsync(
            InferenceProviderState.Starting,
            $"Starting llama-server with -hf {modelReference}…",
            async cancellationToken => await _inferenceProvider
                .StartAsync(modelReference, cancellationToken)
                .ConfigureAwait(true),
            allowStop: true).ConfigureAwait(true);
    }

    private async Task StopProviderAsync()
    {
        if (!CanStopProvider)
        {
            return;
        }

        ClearProviderProgress();
        _responseCancellation?.Cancel();
        _providerOperationCancellation?.Cancel();
        SetProviderTransientState(
            InferenceProviderState.Stopping,
            "Stopping the managed llama-server process…");

        try
        {
            InferenceProviderSnapshot snapshot = await _inferenceProvider
                .StopAsync(CancellationToken.None)
                .ConfigureAwait(true);
            ApplyProviderSnapshot(snapshot);
            ClearError();
        }
        catch (InvalidOperationException exception)
        {
            ErrorMessage = exception.Message;
        }
        catch (IOException exception)
        {
            ErrorMessage = exception.Message;
        }
    }

    private async Task ExecuteProviderOperationAsync(
        InferenceProviderState transientState,
        string transientDetail,
        Func<CancellationToken, Task<InferenceProviderSnapshot>> operation,
        bool allowStop = false)
    {
        ArgumentNullException.ThrowIfNull(operation);

        if (IsProviderBusy)
        {
            return;
        }

        using CancellationTokenSource cancellationSource = new();
        _providerOperationCancellation = cancellationSource;
        IsProviderBusy = true;
        ClearError();
        SetProviderTransientState(transientState, transientDetail);

        if (allowStop)
        {
            OnPropertyChanged(nameof(CanStopProvider));
            StopProviderCommand.NotifyCanExecuteChanged();
        }

        try
        {
            InferenceProviderSnapshot snapshot = await operation(cancellationSource.Token)
                .ConfigureAwait(true);
            ApplyProviderSnapshot(snapshot);
        }
        catch (OperationCanceledException) when (cancellationSource.IsCancellationRequested)
        {
            ClearError();
            InferenceProviderSnapshot snapshot = await _inferenceProvider
                .DetectAsync(CancellationToken.None)
                .ConfigureAwait(true);
            ApplyProviderSnapshot(snapshot);
        }
        catch (PlatformNotSupportedException exception)
        {
            SetProviderFault(exception.Message, InferenceProviderState.Unsupported);
        }
        catch (HttpRequestException exception)
        {
            await RestoreProviderAfterFailureAsync(exception.Message).ConfigureAwait(true);
        }
        catch (InvalidDataException exception)
        {
            await RestoreProviderAfterFailureAsync(exception.Message).ConfigureAwait(true);
        }
        catch (IOException exception)
        {
            await RestoreProviderAfterFailureAsync(exception.Message).ConfigureAwait(true);
        }
        catch (UnauthorizedAccessException exception)
        {
            await RestoreProviderAfterFailureAsync(exception.Message).ConfigureAwait(true);
        }
        catch (ArgumentException exception)
        {
            await RestoreProviderAfterFailureAsync(exception.Message).ConfigureAwait(true);
        }
        catch (InvalidOperationException exception)
        {
            await RestoreProviderAfterFailureAsync(exception.Message).ConfigureAwait(true);
        }
        finally
        {
            if (ReferenceEquals(_providerOperationCancellation, cancellationSource))
            {
                _providerOperationCancellation = null;
            }

            IsProviderBusy = false;
        }
    }

    private async Task RestoreProviderAfterFailureAsync(string errorMessage)
    {
        ErrorMessage = errorMessage;

        try
        {
            InferenceProviderSnapshot snapshot = await _inferenceProvider
                .DetectAsync(CancellationToken.None)
                .ConfigureAwait(true);
            ApplyProviderSnapshot(snapshot);
        }
        catch (PlatformNotSupportedException exception)
        {
            SetProviderFault(
                $"{errorMessage} {exception.Message}",
                InferenceProviderState.Unsupported);
        }
        catch (HttpRequestException exception)
        {
            SetProviderRedetectionFault(errorMessage, exception.Message);
        }
        catch (InvalidDataException exception)
        {
            SetProviderRedetectionFault(errorMessage, exception.Message);
        }
        catch (IOException exception)
        {
            SetProviderRedetectionFault(errorMessage, exception.Message);
        }
        catch (UnauthorizedAccessException exception)
        {
            SetProviderRedetectionFault(errorMessage, exception.Message);
        }
        catch (ArgumentException exception)
        {
            SetProviderRedetectionFault(errorMessage, exception.Message);
        }
        catch (InvalidOperationException exception)
        {
            SetProviderRedetectionFault(errorMessage, exception.Message);
        }
    }

    private void SetProviderRedetectionFault(
        string originalError,
        string detectionError)
    {
        SetProviderFault(
            $"{originalError} Provider re-detection also failed: {detectionError}",
            InferenceProviderState.Faulted);
    }

    private void ApplyProviderProgress(InferenceProviderProgress progress)
    {
        ArgumentNullException.ThrowIfNull(progress);
        _providerProgress = progress;
        RaiseProviderProgressChanged();
    }

    private void ClearProviderProgress()
    {
        if (_providerProgress is null)
        {
            return;
        }

        _providerProgress = null;
        RaiseProviderProgressChanged();
    }

    private void RaiseProviderProgressChanged()
    {
        OnPropertyChanged(nameof(IsProviderProgressVisible));
        OnPropertyChanged(nameof(IsProviderProgressIndeterminate));
        OnPropertyChanged(nameof(ProviderProgressValue));
        OnPropertyChanged(nameof(ProviderProgressText));
        OnPropertyChanged(nameof(ProviderProgressDetailText));
    }

    private void ApplyProviderSnapshot(InferenceProviderSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        _providerSnapshot = snapshot;

        if (!string.IsNullOrWhiteSpace(snapshot.ModelReference))
        {
            ProviderModelReference = snapshot.ModelReference;
        }

        RaiseProviderStateChanged();
    }

    private void SetProviderTransientState(
        InferenceProviderState state,
        string detail)
    {
        _providerSnapshot = new InferenceProviderSnapshot(
            _providerSnapshot?.Name ?? "llama.cpp CUDA",
            state,
            _providerSnapshot?.Version,
            _providerSnapshot?.IsCudaEnabled ?? false,
            _providerSnapshot?.ExecutablePath,
            string.IsNullOrWhiteSpace(ProviderModelReference) ? null : ProviderModelReference,
            _providerSnapshot?.Endpoint,
            detail);
        RaiseProviderStateChanged();
    }

    private void SetProviderFault(string detail, InferenceProviderState state)
    {
        ErrorMessage = detail;
        _providerSnapshot = new InferenceProviderSnapshot(
            _providerSnapshot?.Name ?? "llama.cpp CUDA",
            state,
            _providerSnapshot?.Version,
            _providerSnapshot?.IsCudaEnabled ?? false,
            _providerSnapshot?.ExecutablePath,
            string.IsNullOrWhiteSpace(ProviderModelReference) ? null : ProviderModelReference,
            endpoint: null,
            detail: detail);
        RaiseProviderStateChanged();
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
        MessageViewModel streamingMessage = MessageViewModel.CreateStreamingAssistant();
        Messages.Add(streamingMessage);
        RaiseMessageStateChanged();

        try
        {
            await foreach (ConversationResponseChunk chunk in _streamConversationTurn
                .ExecuteAsync(
                    conversationId,
                    triggeringMessageId,
                    cancellationSource.Token)
                .ConfigureAwait(true))
            {
                streamingMessage.AppendContentDelta(chunk.ContentDelta);
            }

            ClearRetryResponse();
            await ReloadConversationsAsync(conversationId).ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (cancellationSource.IsCancellationRequested)
        {
            RemoveStreamingMessage(streamingMessage);
            ClearError();
        }
        catch
        {
            RemoveStreamingMessage(streamingMessage);
            throw;
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

    private void RemoveStreamingMessage(MessageViewModel streamingMessage)
    {
        if (Messages.Remove(streamingMessage))
        {
            RaiseMessageStateChanged();
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
        catch (HttpRequestException exception)
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

    private void RaiseProviderStateChanged()
    {
        OnPropertyChanged(nameof(IsProviderRunning));
        OnPropertyChanged(nameof(CanDetectProvider));
        OnPropertyChanged(nameof(CanInstallProvider));
        OnPropertyChanged(nameof(CanStartProvider));
        OnPropertyChanged(nameof(CanStopProvider));
        OnPropertyChanged(nameof(IsProviderModelEditable));
        OnPropertyChanged(nameof(ProviderName));
        OnPropertyChanged(nameof(ProviderStatusText));
        OnPropertyChanged(nameof(ProviderDetailText));
        OnPropertyChanged(nameof(ProviderVersionText));
        OnPropertyChanged(nameof(IsProviderProgressVisible));
        OnPropertyChanged(nameof(IsProviderProgressIndeterminate));
        OnPropertyChanged(nameof(ProviderProgressValue));
        OnPropertyChanged(nameof(ProviderProgressText));
        OnPropertyChanged(nameof(ProviderProgressDetailText));
        OnPropertyChanged(nameof(CanSendMessage));
        OnPropertyChanged(nameof(IsComposerEnabled));
        OnPropertyChanged(nameof(ComposerPlaceholder));
        OnPropertyChanged(nameof(ComposerStatusText));
        OnPropertyChanged(nameof(StatusText));
        DetectProviderCommand.NotifyCanExecuteChanged();
        InstallProviderCommand.NotifyCanExecuteChanged();
        StartProviderCommand.NotifyCanExecuteChanged();
        StopProviderCommand.NotifyCanExecuteChanged();
        SendMessageCommand.NotifyCanExecuteChanged();
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
