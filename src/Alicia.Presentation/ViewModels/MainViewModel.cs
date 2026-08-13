using System.Collections.ObjectModel;
using Alicia.Application.Conversations;
using Alicia.Application.Providers;
using Alicia.Domain.Conversations;
using Alicia.Presentation.State;
using Alicia.Presentation.Threading;
using CommunityToolkit.Mvvm.Input;

namespace Alicia.Presentation.ViewModels;

public sealed class MainViewModel : ViewModelBase
{
    private const double ScrollDetachThreshold = 32d;

    private static readonly TimeSpan _defaultResponseStopLockDuration = TimeSpan.FromMilliseconds(1200);
    private static readonly TimeSpan _defaultRetryResponseLockDuration = TimeSpan.FromMilliseconds(700);

    private readonly AppendMessageUseCase _appendMessage;
    private readonly StreamConversationTurnUseCase _streamConversationTurn;
    private readonly CreateConversationUseCase _createConversation;
    private readonly DeleteConversationUseCase _deleteConversation;
    private readonly ListConversationsUseCase _listConversations;
    private readonly LoadConversationUseCase _loadConversation;
    private readonly RenameConversationUseCase _renameConversation;
    private readonly IConversationUiStateStore _conversationUiStateStore;
    private readonly TimeSpan _responseStopLockDuration;
    private readonly TimeSpan _retryResponseLockDuration;
    private CancellationTokenSource? _historySearchCancellation;
    private Task _historySearchTask = Task.CompletedTask;
    private bool _isConversationHistoryAutoCollapsed;
    private bool _isNarrowConversationLayout;
    private bool _isRetryResponseUnlocked;
    private bool _isStopResponseUnlocked;
    private ConversationId? _retryConversationId;
    private MessageId? _retryTriggeringMessageId;
    private CancellationTokenSource? _responseCancellation;
    private CancellationTokenSource? _retryResponseUnlockCancellation;
    private CancellationTokenSource? _providerOperationCancellation;
    private ConversationUiStateSnapshot _conversationUiState = ConversationUiStateSnapshot.Default;
    private int _conversationScrollRestoreRevision;

    public MainViewModel(
        CreateConversationUseCase createConversation,
        AppendMessageUseCase appendMessage,
        StreamConversationTurnUseCase streamConversationTurn,
        LoadConversationUseCase loadConversation,
        ListConversationsUseCase listConversations,
        RenameConversationUseCase renameConversation,
        DeleteConversationUseCase deleteConversation,
        IInferenceProviderRegistry providerRegistry,
        IInferenceProviderConfigurationStore providerConfigurationStore,
        TimeSpan? responseStopLockDuration = null,
        TimeSpan? retryResponseLockDuration = null,
        IConversationUiStateStore? conversationUiStateStore = null,
        bool isReducedMotionEnabled = false)
    {
        ArgumentNullException.ThrowIfNull(createConversation);
        ArgumentNullException.ThrowIfNull(appendMessage);
        ArgumentNullException.ThrowIfNull(streamConversationTurn);
        ArgumentNullException.ThrowIfNull(loadConversation);
        ArgumentNullException.ThrowIfNull(listConversations);
        ArgumentNullException.ThrowIfNull(renameConversation);
        ArgumentNullException.ThrowIfNull(deleteConversation);
        ArgumentNullException.ThrowIfNull(providerRegistry);
        ArgumentNullException.ThrowIfNull(providerConfigurationStore);

        TimeSpan resolvedStopLockDuration = responseStopLockDuration
            ?? _defaultResponseStopLockDuration;
        TimeSpan resolvedRetryLockDuration = retryResponseLockDuration
            ?? _defaultRetryResponseLockDuration;

        if (resolvedStopLockDuration < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(responseStopLockDuration),
                "Response stop lock duration cannot be negative.");
        }

        if (resolvedRetryLockDuration < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(retryResponseLockDuration),
                "Retry response lock duration cannot be negative.");
        }

        _createConversation = createConversation;
        _appendMessage = appendMessage;
        _streamConversationTurn = streamConversationTurn;
        _loadConversation = loadConversation;
        _listConversations = listConversations;
        _renameConversation = renameConversation;
        _deleteConversation = deleteConversation;
        ConversationWorkspace = new ConversationWorkspaceViewModel();
        ConversationHistory = new ConversationHistoryViewModel();
        ConversationStream = new ConversationStreamViewModel(isReducedMotionEnabled);
        Provider = new ProviderViewModel(providerRegistry);
        GenerationSettings = new GenerationSettingsViewModel();
        Model = new ModelViewModel(providerConfigurationStore, GenerationSettings);
        _conversationUiStateStore = conversationUiStateStore
            ?? new TransientConversationUiStateStore();
        _responseStopLockDuration = resolvedStopLockDuration;
        _retryResponseLockDuration = resolvedRetryLockDuration;

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
        ToggleConversationHistoryCommand = new AsyncRelayCommand(ToggleConversationHistoryAsync);
        PauseAutoScrollCommand = new AsyncRelayCommand(
            PauseAutoScrollAsync,
            () => HasSelectedConversation && HasMessages && !IsConversationScrollDetached);
        ScrollToLatestCommand = new AsyncRelayCommand(
            ScrollToLatestAsync,
            () => HasSelectedConversation && IsConversationScrollDetached);
        DetectProviderCommand = new AsyncRelayCommand(DetectProviderAsync, () => CanDetectProvider);
        InstallProviderCommand = new AsyncRelayCommand(InstallProviderAsync, () => CanInstallProvider);
        StartProviderCommand = new AsyncRelayCommand(StartProviderAsync, () => CanStartProvider);
        StopProviderCommand = new AsyncRelayCommand(StopProviderAsync, () => CanStopProvider);
        SaveProviderConfigurationCommand = new AsyncRelayCommand(
            SaveProviderConfigurationAsync,
            () => CanSaveProviderConfiguration);
    }

    public string ApplicationName { get; } = "Alicia";

    public ConversationWorkspaceViewModel ConversationWorkspace { get; }

    public ConversationHistoryViewModel ConversationHistory { get; }

    public ConversationStreamViewModel ConversationStream { get; }

    public ProviderViewModel Provider { get; }

    public ModelViewModel Model { get; }

    public GenerationSettingsViewModel GenerationSettings { get; }

    public ObservableCollection<ConversationListItemViewModel> Conversations =>
        ConversationHistory.Conversations;

    public ObservableCollection<MessageViewModel> Messages => ConversationStream.Messages;

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

    public IAsyncRelayCommand ToggleConversationHistoryCommand { get; }

    public IAsyncRelayCommand PauseAutoScrollCommand { get; }

    public IAsyncRelayCommand ScrollToLatestCommand { get; }

    public IAsyncRelayCommand DetectProviderCommand { get; }

    public IAsyncRelayCommand InstallProviderCommand { get; }

    public IAsyncRelayCommand StartProviderCommand { get; }

    public IAsyncRelayCommand StopProviderCommand { get; }

    public IAsyncRelayCommand SaveProviderConfigurationCommand { get; }

    public string HistorySearchText
    {
        get => ConversationHistory.HistorySearchText;
        set
        {
            if (string.Equals(ConversationHistory.HistorySearchText, value, StringComparison.Ordinal))
            {
                return;
            }

            ConversationHistory.HistorySearchText = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasHistorySearch));
            RestartHistorySearch();
        }
    }

    public ConversationListItemViewModel? SelectedConversation
    {
        get => ConversationWorkspace.SelectedConversation;
        private set
        {
            if (!ConversationWorkspace.SetSelectedConversation(
                value,
                out bool conversationChanged))
            {
                return;
            }

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
        get => ConversationWorkspace.MessageDraft;
        set
        {
            if (string.Equals(ConversationWorkspace.MessageDraft, value, StringComparison.Ordinal))
            {
                return;
            }

            ConversationWorkspace.MessageDraft = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanSendMessage));
            SendMessageCommand.NotifyCanExecuteChanged();
        }
    }

    public IReadOnlyList<InferenceProviderDescriptor> ProviderOptions => Provider.ProviderOptions;

    public InferenceProviderDescriptor? SelectedProvider
    {
        get => Provider.SelectedProvider;
        set
        {
            if (SelectedProvider is not null
                && !IsProviderSelectionEditable
                && !Equals(SelectedProvider, value))
            {
                return;
            }

            if (Equals(SelectedProvider, value))
            {
                return;
            }

            Provider.SelectProvider(value);
            Model.ApplySelectedProvider(value);
            OnPropertyChanged();
            RaiseProviderDraftChanged();
            RaiseProviderStateChanged();
        }
    }

    public string ProviderModelReference
    {
        get => Model.ProviderModelReference;
        set
        {
            if (string.Equals(Model.ProviderModelReference, value, StringComparison.Ordinal))
            {
                return;
            }

            Model.ProviderModelReference = value;
            OnPropertyChanged();
            RaiseProviderConfigurationStateChanged();
        }
    }

    public string ProviderContextSizeText
    {
        get => Model.ProviderContextSizeText;
        set
        {
            if (string.Equals(Model.ProviderContextSizeText, value, StringComparison.Ordinal))
            {
                return;
            }

            Model.ProviderContextSizeText = value;
            OnPropertyChanged();
            RaiseProviderConfigurationStateChanged();
        }
    }

    public string ProviderMaxOutputTokensText
    {
        get => GenerationSettings.ProviderMaxOutputTokensText;
        set => SetGenerationText(
            GenerationSettings.ProviderMaxOutputTokensText,
            value,
            newValue => GenerationSettings.ProviderMaxOutputTokensText = newValue,
            nameof(ProviderMaxOutputTokensText));
    }

    public string ProviderTemperatureText
    {
        get => GenerationSettings.ProviderTemperatureText;
        set => SetGenerationText(
            GenerationSettings.ProviderTemperatureText,
            value,
            newValue => GenerationSettings.ProviderTemperatureText = newValue,
            nameof(ProviderTemperatureText));
    }

    public string ProviderTopPText
    {
        get => GenerationSettings.ProviderTopPText;
        set => SetGenerationText(
            GenerationSettings.ProviderTopPText,
            value,
            newValue => GenerationSettings.ProviderTopPText = newValue,
            nameof(ProviderTopPText));
    }

    public string ProviderTopKText
    {
        get => GenerationSettings.ProviderTopKText;
        set => SetGenerationText(
            GenerationSettings.ProviderTopKText,
            value,
            newValue => GenerationSettings.ProviderTopKText = newValue,
            nameof(ProviderTopKText));
    }

    public string ProviderSeedText
    {
        get => GenerationSettings.ProviderSeedText;
        set => SetGenerationText(
            GenerationSettings.ProviderSeedText,
            value,
            newValue => GenerationSettings.ProviderSeedText = newValue,
            nameof(ProviderSeedText));
    }

    public bool ProviderReasoningEnabled
    {
        get => GenerationSettings.ProviderReasoningEnabled;
        set
        {
            if (GenerationSettings.ProviderReasoningEnabled == value)
            {
                return;
            }

            GenerationSettings.ProviderReasoningEnabled = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsProviderReasoningBudgetEditable));
            RaiseProviderConfigurationStateChanged();
        }
    }

    public string ProviderReasoningBudgetText
    {
        get => GenerationSettings.ProviderReasoningBudgetText;
        set => SetGenerationText(
            GenerationSettings.ProviderReasoningBudgetText,
            value,
            newValue => GenerationSettings.ProviderReasoningBudgetText = newValue,
            nameof(ProviderReasoningBudgetText));
    }

    public bool IsProviderReasoningBudgetEditable => IsProviderSettingsEditable
        && ProviderReasoningEnabled;

    public bool IsBusy
    {
        get => ConversationWorkspace.IsBusy;
        private set
        {
            if (ConversationWorkspace.IsBusy == value)
            {
                return;
            }

            ConversationWorkspace.IsBusy = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsInteractionEnabled));
            OnPropertyChanged(nameof(CanSendMessage));
            OnPropertyChanged(nameof(IsComposerEnabled));
            OnPropertyChanged(nameof(SendButtonLabel));
            OnPropertyChanged(nameof(ComposerStatusText));
            OnPropertyChanged(nameof(StatusText));
            OnPropertyChanged(nameof(CanRetryResponse));
            OnPropertyChanged(nameof(CanSaveProviderConfiguration));
            OnPropertyChanged(nameof(IsProviderSettingsEditable));
            OnPropertyChanged(nameof(IsProviderSelectionEditable));
            OnPropertyChanged(nameof(IsProviderReasoningBudgetEditable));
            SendMessageCommand.NotifyCanExecuteChanged();
            RetryResponseCommand.NotifyCanExecuteChanged();
            SaveProviderConfigurationCommand.NotifyCanExecuteChanged();
            ConversationHistory.NotifyInteractionStateChanged();
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
        get => Provider.IsProviderBusy;
        private set
        {
            if (Provider.IsProviderBusy == value)
            {
                return;
            }

            Provider.IsProviderBusy = value;
            OnPropertyChanged();
            RaiseProviderStateChanged();
        }
    }

    public bool IsProviderRunning => Provider.IsProviderRunning;

    public bool IsProviderSettingsEditable => !IsProviderBusy
        && !IsProviderRunning
        && !IsGeneratingResponse;

    public bool IsProviderSelectionEditable => IsProviderSettingsEditable;

    public bool CanDetectProvider => SelectedProvider is not null
        && !IsProviderBusy
        && !IsGeneratingResponse;

    public bool CanInstallProvider => SelectedProvider is not null
        && !IsProviderBusy
        && !IsGeneratingResponse
        && Provider.Snapshot?.State is InferenceProviderState.Missing
            or InferenceProviderState.Unsupported
            or InferenceProviderState.Faulted;

    public bool CanStartProvider => SelectedProvider is not null
        && !IsProviderBusy
        && !IsGeneratingResponse
        && Provider.Snapshot?.State == InferenceProviderState.Ready
        && !HasProviderConfigurationChanges
        && Model.SavedProviderConfiguration?.HasModelReference == true
        && string.Equals(
            Model.PersistedSelectedProviderId,
            SelectedProvider.Id,
            StringComparison.Ordinal);

    public bool CanStopProvider => IsProviderRunning
        || Provider.Snapshot?.State == InferenceProviderState.Starting;

    public bool IsProviderModelEditable => IsProviderSettingsEditable;

    public bool HasProviderConfigurationChanges =>
        Model.HasConfigurationChanges(SelectedProvider);

    public bool CanSaveProviderConfiguration => IsProviderSettingsEditable
        && SelectedProvider is not null
        && HasProviderConfigurationChanges
        && TryBuildProviderConfiguration(out _, out _);

    public string ProviderConfigurationValidationText => TryBuildProviderConfiguration(
            out _,
            out string? validationError)
        ? string.Empty
        : validationError ?? "Provider settings are invalid.";

    public bool HasProviderConfigurationValidationError =>
        !string.IsNullOrWhiteSpace(ProviderConfigurationValidationText);

    public string ProviderConfigurationStatusText =>
        Model.GetConfigurationStatusText(SelectedProvider);

    public string ProviderFallbackText => ProviderOptions.Count == 0
        ? "Fallback disabled • No inference provider is currently available."
        : "Fallback disabled • Alicia never switches inference providers automatically.";

    public string ProviderName => Provider.ProviderName;

    public string ProviderStatusText => Provider.ProviderStatusText;

    public string ProviderDetailText => Provider.ProviderDetailText;

    public string ProviderVersionText => Provider.ProviderVersionText;

    public bool IsProviderProgressVisible => Provider.IsProviderProgressVisible;

    public bool IsProviderProgressIndeterminate => Provider.IsProviderProgressIndeterminate;

    public double ProviderProgressValue => Provider.ProviderProgressValue;

    public string ProviderProgressText => Provider.ProviderProgressText;

    public string ProviderProgressDetailText => Provider.ProviderProgressDetailText;

    public bool IsSendingMessage
    {
        get => ConversationStream.IsSendingMessage;
        private set
        {
            if (ConversationStream.IsSendingMessage == value)
            {
                return;
            }

            ConversationStream.IsSendingMessage = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SendButtonLabel));
            OnPropertyChanged(nameof(ComposerStatusText));
            OnPropertyChanged(nameof(StatusText));
        }
    }

    public bool IsGeneratingResponse
    {
        get => ConversationStream.IsGeneratingResponse;
        private set
        {
            if (ConversationStream.IsGeneratingResponse == value)
            {
                return;
            }

            ConversationStream.IsGeneratingResponse = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanStopResponse));
            OnPropertyChanged(nameof(CanRetryResponse));
            OnPropertyChanged(nameof(ComposerStatusText));
            OnPropertyChanged(nameof(StatusText));
            StopResponseCommand.NotifyCanExecuteChanged();
            RetryResponseCommand.NotifyCanExecuteChanged();
            RaiseProviderStateChanged();
        }
    }

    public bool CanStopResponse => IsGeneratingResponse
        && _isStopResponseUnlocked
        && _responseCancellation is { IsCancellationRequested: false };

    public bool CanRetryResponse => !IsBusy
        && !IsGeneratingResponse
        && _isRetryResponseUnlocked
        && _retryConversationId is ConversationId retryConversationId
        && _retryTriggeringMessageId is not null
        && SelectedConversation?.Id == retryConversationId;

    public bool IsInitialized
    {
        get => ConversationWorkspace.IsInitialized;
        private set
        {
            if (ConversationWorkspace.IsInitialized == value)
            {
                return;
            }

            ConversationWorkspace.IsInitialized = value;
            OnPropertyChanged();
            RaiseEmptyStateChanged();
        }
    }

    public bool IsDeleteConfirmationVisible
    {
        get => ConversationWorkspace.IsDeleteConfirmationVisible;
        private set
        {
            if (ConversationWorkspace.IsDeleteConfirmationVisible == value)
            {
                return;
            }

            ConversationWorkspace.IsDeleteConfirmationVisible = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanSendMessage));
            OnPropertyChanged(nameof(IsComposerEnabled));
            SendMessageCommand.NotifyCanExecuteChanged();
        }
    }

    public string? ErrorMessage
    {
        get => ConversationWorkspace.ErrorMessage;
        private set
        {
            if (string.Equals(ConversationWorkspace.ErrorMessage, value, StringComparison.Ordinal))
            {
                return;
            }

            ConversationWorkspace.ErrorMessage = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasError));
        }
    }

    public bool HasError => ConversationWorkspace.HasError;

    public bool IsConversationHistoryExpanded
    {
        get => ConversationHistory.IsConversationHistoryExpanded;
        private set
        {
            if (ConversationHistory.IsConversationHistoryExpanded == value)
            {
                return;
            }

            ConversationHistory.IsConversationHistoryExpanded = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsConversationHistoryCollapsed));
        }
    }

    public bool IsConversationHistoryCollapsed => ConversationHistory.IsConversationHistoryCollapsed;

    public bool IsReducedMotionEnabled => ConversationStream.IsReducedMotionEnabled;

    public ConversationScrollMode CurrentConversationScrollMode => SelectedConversation is null
        ? ConversationScrollMode.Following
        : _conversationUiState
            .GetConversationScrollState(SelectedConversation.Id)
            .Mode;

    public double CurrentConversationScrollOffset => SelectedConversation is null
        ? 0
        : _conversationUiState
            .GetConversationScrollState(SelectedConversation.Id)
            .VerticalOffset;

    public bool IsConversationScrollDetached =>
        CurrentConversationScrollMode == ConversationScrollMode.Detached;

    public bool ShowPauseAutoScrollButton => HasMessages && !IsConversationScrollDetached;

    public bool ShowScrollToLatestButton => HasMessages && IsConversationScrollDetached;

    public int ConversationScrollRestoreRevision => _conversationScrollRestoreRevision;

    public bool IsHistorySearchBusy
    {
        get => ConversationHistory.IsHistorySearchBusy;
        private set
        {
            if (ConversationHistory.IsHistorySearchBusy == value)
            {
                return;
            }

            ConversationHistory.IsHistorySearchBusy = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ShowNoHistorySearchResults));
        }
    }

    public bool HasHistorySearch => !string.IsNullOrWhiteSpace(HistorySearchText);

    public bool HasConversationHistory => ConversationHistory.HasConversationHistory;

    public bool IsHistoryEmpty => IsInitialized && !HasConversationHistory;

    public bool ShowNoHistorySearchResults => IsInitialized
        && HasConversationHistory
        && HasHistorySearch
        && !IsHistorySearchBusy
        && Conversations.Count == 0;

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
        ? "Start the selected provider to enable local AI chat"
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
                    ? "Start the selected provider before sending messages"
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

        await LoadConversationUiStateAsync().ConfigureAwait(true);
        await ExecuteOperationAsync(LoadProviderConfigurationAsync).ConfigureAwait(true);

        if (SelectedProvider is not null)
        {
            await DetectProviderAsync().ConfigureAwait(true);
        }

        await ExecuteOperationAsync(async () =>
        {
            await ReloadConversationsAsync(preferredConversationId: null).ConfigureAwait(true);
        }).ConfigureAwait(true);

        IsInitialized = true;
    }

    public void SetConversationHistoryNarrowLayout(bool isNarrow)
    {
        if (_isNarrowConversationLayout == isNarrow)
        {
            return;
        }

        _isNarrowConversationLayout = isNarrow;
        _isConversationHistoryAutoCollapsed = isNarrow
            && _conversationUiState.IsConversationHistoryExpanded;
        ApplyConversationHistoryExpansion();
    }

    public void ReportConversationScrollPosition(
        double verticalOffset,
        double maximumVerticalOffset,
        bool userMovedUp)
    {
        if (SelectedConversation is null)
        {
            return;
        }

        if (!double.IsFinite(verticalOffset) || verticalOffset < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(verticalOffset));
        }

        if (!double.IsFinite(maximumVerticalOffset) || maximumVerticalOffset < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumVerticalOffset));
        }

        double clampedOffset = Math.Min(verticalOffset, maximumVerticalOffset);
        ConversationScrollState current = _conversationUiState
            .GetConversationScrollState(SelectedConversation.Id);
        ConversationScrollMode mode = current.Mode;
        double distanceFromBottom = Math.Max(0, maximumVerticalOffset - clampedOffset);

        if (mode == ConversationScrollMode.Following
            && userMovedUp
            && distanceFromBottom >= ScrollDetachThreshold)
        {
            mode = ConversationScrollMode.Detached;
        }

        double storedOffset = mode == ConversationScrollMode.Detached
            ? clampedOffset
            : maximumVerticalOffset;
        ConversationScrollState updated = new(mode, storedOffset);

        if (updated == current)
        {
            return;
        }

        _conversationUiState = _conversationUiState.WithConversationScrollState(
            SelectedConversation.Id,
            updated);
        RaiseConversationScrollStateChanged();
    }

    public async Task PersistConversationUiStateAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            await _conversationUiStateStore
                .SaveAsync(_conversationUiState, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private async Task LoadConversationUiStateAsync()
    {
        _conversationUiState = await _conversationUiStateStore
            .LoadAsync()
            .ConfigureAwait(true);
        _isConversationHistoryAutoCollapsed = _isNarrowConversationLayout
            && _conversationUiState.IsConversationHistoryExpanded;
        ApplyConversationHistoryExpansion();
        RaiseConversationScrollStateChanged();
    }

    private void ApplyConversationHistoryExpansion()
    {
        IsConversationHistoryExpanded = _conversationUiState.IsConversationHistoryExpanded
            && !_isConversationHistoryAutoCollapsed;
    }

    private async Task PauseAutoScrollAsync()
    {
        if (SelectedConversation is null || !HasMessages)
        {
            return;
        }

        ConversationScrollState current = _conversationUiState
            .GetConversationScrollState(SelectedConversation.Id);

        if (current.Mode == ConversationScrollMode.Detached)
        {
            return;
        }

        _conversationUiState = _conversationUiState.WithConversationScrollState(
            SelectedConversation.Id,
            new ConversationScrollState(
                ConversationScrollMode.Detached,
                current.VerticalOffset));
        RaiseConversationScrollStateChanged();
        await PersistConversationUiStateAsync().ConfigureAwait(true);
    }

    private async Task ScrollToLatestAsync()
    {
        if (SelectedConversation is null)
        {
            return;
        }

        SetConversationScrollFollowing(SelectedConversation.Id, requestRestore: true);
        await PersistConversationUiStateAsync().ConfigureAwait(true);
    }

    private void SetConversationScrollFollowing(
        ConversationId conversationId,
        bool requestRestore)
    {
        ConversationScrollState current = _conversationUiState
            .GetConversationScrollState(conversationId);

        if (current.Mode != ConversationScrollMode.Following)
        {
            _conversationUiState = _conversationUiState.WithConversationScrollState(
                conversationId,
                new ConversationScrollState(
                    ConversationScrollMode.Following,
                    current.VerticalOffset));
            RaiseConversationScrollStateChanged();
        }

        if (requestRestore)
        {
            RequestConversationScrollRestore();
        }
    }

    private void RequestConversationScrollRestore()
    {
        _conversationScrollRestoreRevision++;
        OnPropertyChanged(nameof(ConversationScrollRestoreRevision));
    }

    private void RaiseConversationScrollStateChanged()
    {
        OnPropertyChanged(nameof(CurrentConversationScrollMode));
        OnPropertyChanged(nameof(CurrentConversationScrollOffset));
        OnPropertyChanged(nameof(IsConversationScrollDetached));
        OnPropertyChanged(nameof(ShowPauseAutoScrollButton));
        OnPropertyChanged(nameof(ShowScrollToLatestButton));
        PauseAutoScrollCommand.NotifyCanExecuteChanged();
        ScrollToLatestCommand.NotifyCanExecuteChanged();
    }

    private async Task LoadProviderConfigurationAsync()
    {
        await Model.LoadAsync(ProviderOptions).ConfigureAwait(true);

        string? selectedProviderId = Model.PersistedSelectedProviderId;
        InferenceProviderDescriptor? selected = selectedProviderId is null
            ? null
            : ProviderOptions.FirstOrDefault(descriptor => string.Equals(
                descriptor.Id,
                selectedProviderId,
                StringComparison.Ordinal));

        if (selected is not null)
        {
            Provider.PersistSelection(selected.Id);
        }

        SelectedProvider = selected ?? (ProviderOptions.Count == 0 ? null : ProviderOptions[0]);
    }

    private void RaiseProviderDraftChanged()
    {
        OnPropertyChanged(nameof(ProviderModelReference));
        OnPropertyChanged(nameof(ProviderContextSizeText));
        OnPropertyChanged(nameof(ProviderMaxOutputTokensText));
        OnPropertyChanged(nameof(ProviderTemperatureText));
        OnPropertyChanged(nameof(ProviderTopPText));
        OnPropertyChanged(nameof(ProviderTopKText));
        OnPropertyChanged(nameof(ProviderSeedText));
        OnPropertyChanged(nameof(ProviderReasoningEnabled));
        OnPropertyChanged(nameof(ProviderReasoningBudgetText));
        OnPropertyChanged(nameof(IsProviderReasoningBudgetEditable));
        RaiseProviderConfigurationStateChanged();
    }

    private void SetGenerationText(
        string currentValue,
        string newValue,
        Action<string> setter,
        string propertyName)
    {
        if (string.Equals(currentValue, newValue, StringComparison.Ordinal))
        {
            return;
        }

        setter(newValue);
        OnPropertyChanged(propertyName);
        RaiseProviderConfigurationStateChanged();
    }

    private async Task SaveProviderConfigurationAsync()
    {
        if (!CanSaveProviderConfiguration || SelectedProvider is null)
        {
            return;
        }

        await ExecuteOperationAsync(async () =>
        {
            InferenceProviderConfiguration configuration = await Model
                .SaveAsync(SelectedProvider)
                .ConfigureAwait(true);
            Provider.PersistSelection(configuration.ProviderId);
            RaiseProviderConfigurationStateChanged();
        }).ConfigureAwait(true);
    }

    private IInferenceProviderRuntime GetSelectedProviderRuntime()
    {
        return Provider.GetSelectedProviderRuntime();
    }

    private bool TryBuildProviderConfiguration(
        out InferenceProviderConfiguration? configuration,
        out string? validationError)
    {
        return Model.TryBuildProviderConfiguration(
            SelectedProvider,
            out configuration,
            out validationError);
    }

    private void RaiseProviderConfigurationStateChanged()
    {
        OnPropertyChanged(nameof(HasProviderConfigurationChanges));
        OnPropertyChanged(nameof(CanSaveProviderConfiguration));
        OnPropertyChanged(nameof(ProviderConfigurationValidationText));
        OnPropertyChanged(nameof(HasProviderConfigurationValidationError));
        OnPropertyChanged(nameof(ProviderConfigurationStatusText));
        OnPropertyChanged(nameof(CanStartProvider));
        OnPropertyChanged(nameof(IsProviderReasoningBudgetEditable));
        OnPropertyChanged(nameof(ComposerStatusText));
        SaveProviderConfigurationCommand.NotifyCanExecuteChanged();
        StartProviderCommand.NotifyCanExecuteChanged();
    }

    private async Task DetectProviderAsync()
    {
        ClearProviderProgress();

        await ExecuteProviderOperationAsync(
            InferenceProviderState.Detecting,
            $"Detecting {ProviderName}…",
            async cancellationToken => await GetSelectedProviderRuntime()
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
        SerializedProgress<InferenceProviderProgress> progress = new(ApplyProviderProgress);

        await ExecuteProviderOperationAsync(
            InferenceProviderState.Installing,
            $"Preparing {ProviderName} installation…",
            async cancellationToken => await GetSelectedProviderRuntime()
                .InstallAsync(progress, cancellationToken)
                .ConfigureAwait(true)).ConfigureAwait(true);
    }

    private async Task StartProviderAsync()
    {
        if (!CanStartProvider)
        {
            return;
        }

        InferenceProviderConfiguration configuration = Model.SavedProviderConfiguration
            ?? throw new InvalidOperationException(
                "Save provider settings before starting the local model.");
        ClearProviderProgress();

        await ExecuteProviderOperationAsync(
            InferenceProviderState.Starting,
            $"Starting the selected provider with {configuration.ModelReference}…",
            async cancellationToken => await GetSelectedProviderRuntime()
                .StartAsync(configuration, cancellationToken)
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
            $"Stopping {ProviderName}…");

        try
        {
            InferenceProviderSnapshot snapshot = await GetSelectedProviderRuntime()
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
            InferenceProviderSnapshot snapshot = await GetSelectedProviderRuntime()
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
            InferenceProviderSnapshot snapshot = await GetSelectedProviderRuntime()
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
        Provider.ApplyProgress(progress);
        RaiseProviderProgressChanged();
    }

    private void ClearProviderProgress()
    {
        Provider.ClearProgress();
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
        Provider.ApplySnapshot(snapshot);
        RaiseProviderStateChanged();
    }

    private void SetProviderTransientState(
        InferenceProviderState state,
        string detail)
    {
        Provider.SetTransientState(state, detail, ProviderModelReference);
        RaiseProviderStateChanged();
    }

    private void SetProviderFault(string detail, InferenceProviderState state)
    {
        ErrorMessage = detail;
        Provider.SetFault(detail, state, ProviderModelReference);
        RaiseProviderStateChanged();
    }

    private async Task CreateConversationAsync()
    {
        HistorySearchText = string.Empty;

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

        SetConversationScrollFollowing(conversationId, requestRestore: true);
        await PersistConversationUiStateAsync().ConfigureAwait(true);

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
        LockStopResponseTemporarily(cancellationSource.Token);
        MessageViewModel streamingMessage = ConversationStream.AddStreamingAssistant();
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
                if (chunk.Kind == ConversationResponseChunkKind.Reasoning)
                {
                    streamingMessage.AppendReasoningDelta(chunk.TextDelta);
                }
                else
                {
                    streamingMessage.AppendContentDelta(chunk.TextDelta);
                }
            }

            string[] reasoningSteps = streamingMessage.CaptureReasoningSteps();
            ClearRetryResponse();
            await ReloadConversationsAsync(conversationId).ConfigureAwait(true);

            if (reasoningSteps.Length > 0)
            {
                Messages.LastOrDefault(message => message.IsAssistant)
                    ?.SetReasoningSnapshot(reasoningSteps);
            }
        }
        catch (OperationCanceledException) when (cancellationSource.IsCancellationRequested)
        {
            RemoveStreamingMessage(streamingMessage);
            ClearError();
            ScheduleRetryResponseUnlock();
        }
        catch
        {
            RemoveStreamingMessage(streamingMessage);
            ScheduleRetryResponseUnlock();
            throw;
        }
        finally
        {
            if (!cancellationSource.IsCancellationRequested)
            {
                cancellationSource.Cancel();
            }

            IsGeneratingResponse = false;

            if (ReferenceEquals(_responseCancellation, cancellationSource))
            {
                _responseCancellation = null;
            }

            _isStopResponseUnlocked = false;
            OnPropertyChanged(nameof(CanStopResponse));
            StopResponseCommand.NotifyCanExecuteChanged();
        }
    }

    private void LockStopResponseTemporarily(CancellationToken cancellationToken)
    {
        _isStopResponseUnlocked = _responseStopLockDuration == TimeSpan.Zero;
        OnPropertyChanged(nameof(CanStopResponse));
        StopResponseCommand.NotifyCanExecuteChanged();

        if (!_isStopResponseUnlocked)
        {
            _ = UnlockStopResponseAfterDelayAsync(cancellationToken);
        }
    }

    private async Task UnlockStopResponseAfterDelayAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(_responseStopLockDuration, cancellationToken).ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return;
        }

        if (!IsGeneratingResponse || cancellationToken.IsCancellationRequested)
        {
            return;
        }

        _isStopResponseUnlocked = true;
        OnPropertyChanged(nameof(CanStopResponse));
        StopResponseCommand.NotifyCanExecuteChanged();
    }

    private void ScheduleRetryResponseUnlock()
    {
        _retryResponseUnlockCancellation?.Cancel();
        _retryResponseUnlockCancellation = null;
        _isRetryResponseUnlocked = _retryResponseLockDuration == TimeSpan.Zero;
        OnPropertyChanged(nameof(CanRetryResponse));
        RetryResponseCommand.NotifyCanExecuteChanged();

        if (_isRetryResponseUnlocked)
        {
            return;
        }

        CancellationTokenSource cancellationSource = new();
        _retryResponseUnlockCancellation = cancellationSource;
        _ = UnlockRetryResponseAfterDelayAsync(cancellationSource);
    }

    private async Task UnlockRetryResponseAfterDelayAsync(
        CancellationTokenSource cancellationSource)
    {
        try
        {
            await Task.Delay(
                _retryResponseLockDuration,
                cancellationSource.Token).ConfigureAwait(true);

            if (ReferenceEquals(_retryResponseUnlockCancellation, cancellationSource)
                && !cancellationSource.IsCancellationRequested)
            {
                _retryResponseUnlockCancellation = null;
                _isRetryResponseUnlocked = true;
                OnPropertyChanged(nameof(CanRetryResponse));
                RetryResponseCommand.NotifyCanExecuteChanged();
            }
        }
        catch (OperationCanceledException) when (cancellationSource.IsCancellationRequested)
        {
        }
        finally
        {
            if (ReferenceEquals(_retryResponseUnlockCancellation, cancellationSource))
            {
                _retryResponseUnlockCancellation = null;
            }

            cancellationSource.Dispose();
        }
    }

    private void RemoveStreamingMessage(MessageViewModel streamingMessage)
    {
        if (ConversationStream.Remove(streamingMessage))
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

            _conversationUiState = _conversationUiState.WithoutConversation(conversationId);
            await PersistConversationUiStateAsync().ConfigureAwait(true);

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

    private async Task ChangeConversationIdentityAsync(
        ConversationListItemViewModel conversation,
        ConversationVisualIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(conversation);
        ArgumentNullException.ThrowIfNull(identity);

        _conversationUiState = _conversationUiState.WithConversationIdentity(
            conversation.Id,
            identity);
        await PersistConversationUiStateAsync().ConfigureAwait(true);
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
        _historySearchCancellation?.Cancel();

        IReadOnlyList<ConversationSummary> summaries = await _listConversations
            .ExecuteAsync()
            .ConfigureAwait(true);

        List<ConversationListItemViewModel> items = [];

        foreach (ConversationSummary summary in summaries)
        {
            items.Add(new ConversationListItemViewModel(
                summary,
                SelectConversationAsync,
                BeginRenameConversationAsync,
                SaveRenameConversationAsync,
                RequestDeleteConversationAsync,
                LoadConversationPreviewAsync,
                _conversationUiState.GetConversationIdentity(summary.Id),
                ChangeConversationIdentityAsync,
                () => IsInteractionEnabled));
        }

        ConversationHistory.ReplaceAll(items);
        ApplyVisibleConversationItems(ConversationHistory.AllConversations);

        if (!HasConversationHistory)
        {
            ClearSelection();
            return;
        }

        RestartHistorySearch();
        await _historySearchTask.ConfigureAwait(true);

        ConversationListItemViewModel? target = preferredConversationId is ConversationId preferredId
            ? Conversations.FirstOrDefault(item => item.Id == preferredId)
            : null;

        target ??= HasHistorySearch
            ? Conversations.FirstOrDefault()
            : ConversationHistory.AllConversations[0];

        if (target is null)
        {
            ClearSelection();
            return;
        }

        await LoadConversationAsync(target).ConfigureAwait(true);
    }

    private async Task LoadConversationAsync(ConversationListItemViewModel item)
    {
        Conversation conversation = await _loadConversation
            .ExecuteAsync(item.Id)
            .ConfigureAwait(true);

        CancelAllRenames();
        SelectedConversation = item;
        ConversationStream.LoadMessages(conversation.Messages);

        IsDeleteConfirmationVisible = false;
        RaiseMessageStateChanged();
        RequestConversationScrollRestore();
    }

    private void ClearSelection()
    {
        CancelAllRenames();
        SelectedConversation = null;
        ConversationStream.ClearMessages();
        IsDeleteConfirmationVisible = false;
        RaiseMessageStateChanged();
    }

    private void CancelAllRenames()
    {
        ConversationHistory.CancelAllRenames();
    }

    private async Task ToggleConversationHistoryAsync()
    {
        if (_isConversationHistoryAutoCollapsed)
        {
            _isConversationHistoryAutoCollapsed = false;
            ApplyConversationHistoryExpansion();
            return;
        }

        bool isExpanded = !IsConversationHistoryExpanded;
        _conversationUiState = _conversationUiState.WithHistoryExpanded(isExpanded);
        _isConversationHistoryAutoCollapsed = false;
        ApplyConversationHistoryExpansion();
        await PersistConversationUiStateAsync().ConfigureAwait(true);
    }

    private void RestartHistorySearch()
    {
        _historySearchCancellation?.Cancel();

        ConversationHistory.ResetPreviews();

        string query = HistorySearchText.Trim();

        if (query.Length == 0)
        {
            _historySearchCancellation = null;
            _historySearchTask = Task.CompletedTask;
            IsHistorySearchBusy = false;
            ApplyVisibleConversationItems(ConversationHistory.AllConversations);
            return;
        }

        CancellationTokenSource cancellationSource = new();
        _historySearchCancellation = cancellationSource;
        IsHistorySearchBusy = true;
        _historySearchTask = ApplyHistorySearchAsync(query, cancellationSource);
    }

    private async Task ApplyHistorySearchAsync(
        string query,
        CancellationTokenSource cancellationSource)
    {
        List<(ConversationListItemViewModel Item, int Score)> matches = [];

        try
        {
            foreach (ConversationListItemViewModel item in ConversationHistory.AllConversations)
            {
                cancellationSource.Token.ThrowIfCancellationRequested();

                int titleScore = GetTitleSearchScore(item.Title, query);

                if (titleScore > 0)
                {
                    matches.Add((item, titleScore));
                    continue;
                }

                Conversation conversation;

                try
                {
                    conversation = await _loadConversation
                        .ExecuteAsync(item.Id, cancellationSource.Token)
                        .ConfigureAwait(true);
                }
                catch (KeyNotFoundException)
                {
                    continue;
                }

                int contentMatchIndex = FindContentMatch(conversation.Messages, query);

                if (contentMatchIndex < 0)
                {
                    continue;
                }

                item.SetSearchPreview(BuildConversationPreview(
                    conversation.Messages,
                    contentMatchIndex));
                matches.Add((item, 10));
            }

            cancellationSource.Token.ThrowIfCancellationRequested();

            if (!ReferenceEquals(_historySearchCancellation, cancellationSource))
            {
                return;
            }

            IReadOnlyList<ConversationListItemViewModel> ordered = matches
                .OrderByDescending(match => match.Score)
                .ThenByDescending(match => match.Item.UpdatedAt)
                .Select(match => match.Item)
                .ToArray();

            ApplyVisibleConversationItems(ordered);
        }
        catch (OperationCanceledException) when (cancellationSource.IsCancellationRequested)
        {
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
        finally
        {
            if (ReferenceEquals(_historySearchCancellation, cancellationSource))
            {
                _historySearchCancellation = null;
                IsHistorySearchBusy = false;
                RaiseHistoryStateChanged();
            }

            cancellationSource.Dispose();
        }
    }

    private async Task<IReadOnlyList<ConversationPreviewMessageViewModel>> LoadConversationPreviewAsync(
        ConversationListItemViewModel item,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(item);

        Conversation conversation = await _loadConversation
            .ExecuteAsync(item.Id, cancellationToken)
            .ConfigureAwait(true);

        return BuildConversationPreview(conversation.Messages, focusedIndex: null);
    }

    private void ApplyVisibleConversationItems(
        IEnumerable<ConversationListItemViewModel> items)
    {
        ConversationHistory.ApplyVisibleItems(items);
        RaiseHistoryStateChanged();
    }

    private static int GetTitleSearchScore(string title, string query)
    {
        if (string.Equals(title, query, StringComparison.OrdinalIgnoreCase))
        {
            return 300;
        }

        if (title.StartsWith(query, StringComparison.OrdinalIgnoreCase))
        {
            return 200;
        }

        return title.Contains(query, StringComparison.OrdinalIgnoreCase)
            ? 100
            : 0;
    }

    private static int FindContentMatch(
        IReadOnlyList<ChatMessage> messages,
        string query)
    {
        for (int index = 0; index < messages.Count; index++)
        {
            if (messages[index].Content.Contains(
                query,
                StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }
        }

        return -1;
    }

    private static ConversationPreviewMessageViewModel[] BuildConversationPreview(
            IReadOnlyList<ChatMessage> messages,
            int? focusedIndex)
    {
        if (messages.Count == 0)
        {
            return [];
        }

        int maximumStart = Math.Max(0, messages.Count - 3);
        int startIndex = focusedIndex is int focus
            ? Math.Clamp(focus - 1, 0, maximumStart)
            : maximumStart;
        int count = Math.Min(3, messages.Count - startIndex);
        ConversationPreviewMessageViewModel[] preview =
            new ConversationPreviewMessageViewModel[count];

        for (int offset = 0; offset < count; offset++)
        {
            preview[offset] = new ConversationPreviewMessageViewModel(
                messages[startIndex + offset]);
        }

        return preview;
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
        _retryResponseUnlockCancellation?.Cancel();
        _retryResponseUnlockCancellation = null;
        _retryConversationId = conversationId;
        _retryTriggeringMessageId = triggeringMessageId;
        _isRetryResponseUnlocked = false;
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

        _retryResponseUnlockCancellation?.Cancel();
        _retryResponseUnlockCancellation = null;
        _retryConversationId = null;
        _retryTriggeringMessageId = null;
        _isRetryResponseUnlocked = false;
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
        OnPropertyChanged(nameof(IsProviderSettingsEditable));
        OnPropertyChanged(nameof(IsProviderSelectionEditable));
        OnPropertyChanged(nameof(IsProviderReasoningBudgetEditable));
        OnPropertyChanged(nameof(CanSaveProviderConfiguration));
        OnPropertyChanged(nameof(HasProviderConfigurationChanges));
        OnPropertyChanged(nameof(ProviderConfigurationValidationText));
        OnPropertyChanged(nameof(HasProviderConfigurationValidationError));
        OnPropertyChanged(nameof(ProviderConfigurationStatusText));
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
        SaveProviderConfigurationCommand.NotifyCanExecuteChanged();
        SendMessageCommand.NotifyCanExecuteChanged();
    }

    private void RaiseHistoryStateChanged()
    {
        OnPropertyChanged(nameof(HasConversationHistory));
        OnPropertyChanged(nameof(ShowNoHistorySearchResults));
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
        RaiseConversationScrollStateChanged();
        RaiseMessageStateChanged();
        RaiseEmptyStateChanged();
    }

    private void RaiseMessageStateChanged()
    {
        OnPropertyChanged(nameof(HasMessages));
        OnPropertyChanged(nameof(IsSelectedConversationEmpty));
        OnPropertyChanged(nameof(SelectedConversationMeta));
        OnPropertyChanged(nameof(ShowPauseAutoScrollButton));
        OnPropertyChanged(nameof(ShowScrollToLatestButton));
        PauseAutoScrollCommand.NotifyCanExecuteChanged();
        ScrollToLatestCommand.NotifyCanExecuteChanged();
    }

    private void RaiseEmptyStateChanged()
    {
        OnPropertyChanged(nameof(IsHistoryEmpty));
        OnPropertyChanged(nameof(ShowNoHistorySearchResults));
        OnPropertyChanged(nameof(IsSelectedConversationEmpty));
        OnPropertyChanged(nameof(ShowNoSelectionState));
        OnPropertyChanged(nameof(ShowLoadingState));
    }
}
