using System.Collections.ObjectModel;
using System.ComponentModel;
using Alicia.Application.Conversations;
using Alicia.Application.Generations;
using Alicia.Application.Providers;
using Alicia.Domain.Conversations;
using Alicia.Presentation.State;
using Alicia.Presentation.Threading;
using CommunityToolkit.Mvvm.Input;

namespace Alicia.Presentation.ViewModels;

public sealed class MainViewModel : ViewModelBase
{
    private enum ProviderMaintenanceConfirmation
    {
        None = 0,
        CleanupRetainedReleases = 1,
        UninstallRuntime = 2,
        UninstallRuntimeAndModelCache = 3,
    }

    private const double ScrollDetachThreshold = 32d;

    private static readonly TimeSpan _defaultResponseStopLockDuration = TimeSpan.FromMilliseconds(1200);
    private static readonly TimeSpan _defaultRetryResponseLockDuration = TimeSpan.FromMilliseconds(700);
    private static readonly TimeSpan _defaultGenerationProfileDraftAutosaveDelay = TimeSpan.FromMilliseconds(400);

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
    private readonly TimeSpan _generationProfileDraftAutosaveDelay;
    private readonly object _generationProfileDraftAutosaveQueueSync = new();
    private Task _generationProfileDraftAutosaveQueueTail = Task.CompletedTask;
    private CancellationTokenSource? _historySearchCancellation;
    private Task _historySearchTask = Task.CompletedTask;
    private bool _isNarrowHistoryOverlayOpen;
    private bool _isNarrowConversationLayout;
    private bool _isRetryResponseUnlocked;
    private bool _isStopResponseUnlocked;
    private ConversationId? _retryConversationId;
    private MessageId? _retryTriggeringMessageId;
    private CancellationTokenSource? _responseCancellation;
    private CancellationTokenSource? _retryResponseUnlockCancellation;
    private CancellationTokenSource? _providerOperationCancellation;
    private ProviderMaintenanceConfirmation _providerMaintenanceConfirmation;
    private ConversationUiStateSnapshot _conversationUiState = ConversationUiStateSnapshot.Default;
    private ConversationBranchId? _selectedConversationBranchId;
    private int _conversationScrollRestoreRevision;
    private CancellationTokenSource? _generationProfileDraftAutosaveCancellation;
    private Task _generationProfileDraftAutosaveTask = Task.CompletedTask;
    private bool _suppressGenerationProfileDraftAutosave;
    private bool _isGenerationProfileSendGateVisible;
    private ConversationId? _generationProfileSendConversationId;
    private ConversationBranchId? _generationProfileSendBranchId;
    private GenerationProfileId? _generationProfileSendProfileId;
    private string? _generationProfileSendContent;
    private string? _generationProfileSendProfileName;
    private bool _generationProfileSendDraftIsStale;

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
        bool isReducedMotionEnabled = false,
        IGenerationProfileCatalogStore? generationProfileCatalogStore = null,
        IConversationBranchGenerationSelectionStore? conversationGenerationSelectionStore = null,
        TimeProvider? generationProfileTimeProvider = null,
        TimeSpan? generationProfileDraftAutosaveDelay = null,
        IInferenceModelLibraryStore? inferenceModelLibraryStore = null)
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
        TimeSpan resolvedGenerationProfileDraftAutosaveDelay = generationProfileDraftAutosaveDelay
            ?? _defaultGenerationProfileDraftAutosaveDelay;

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

        if (resolvedGenerationProfileDraftAutosaveDelay < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(generationProfileDraftAutosaveDelay),
                "Generation-profile draft autosave delay cannot be negative.");
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
        Model = new ModelViewModel(
            providerConfigurationStore,
            GenerationSettings,
            generationProfileCatalogStore,
            conversationGenerationSelectionStore,
            generationProfileTimeProvider,
            inferenceModelLibraryStore);
        GenerationDualSelector = new GenerationDualSelectorViewModel(
            inferenceModelLibraryStore,
            generationProfileCatalogStore,
            conversationGenerationSelectionStore);
        Model.ProfileEditor.PropertyChanged += OnGenerationProfileEditorPropertyChanged;
        Model.ProfileHistory.PropertyChanged += OnGenerationProfileHistoryPropertyChanged;
        GenerationDualSelector.PropertyChanged += OnGenerationDualSelectorPropertyChanged;
        ConfigurationGate = new ConversationConfigurationGateViewModel();
        _conversationUiStateStore = conversationUiStateStore
            ?? new TransientConversationUiStateStore();
        _responseStopLockDuration = resolvedStopLockDuration;
        _retryResponseLockDuration = resolvedRetryLockDuration;
        _generationProfileDraftAutosaveDelay = resolvedGenerationProfileDraftAutosaveDelay;

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
        CheckProviderUpdateCommand = new AsyncRelayCommand(
            CheckProviderUpdateAsync,
            () => CanCheckProviderUpdate);
        UpdateProviderCommand = new AsyncRelayCommand(
            UpdateProviderAsync,
            () => CanUpdateProvider);
        InspectProviderStorageCommand = new AsyncRelayCommand(
            InspectProviderStorageAsync,
            () => CanInspectProviderStorage);
        RequestCleanupRetainedProviderReleasesCommand = new RelayCommand(
            RequestCleanupRetainedProviderReleases,
            () => CanCleanupRetainedProviderReleases);
        RequestUninstallProviderRuntimeCommand = new RelayCommand(
            RequestUninstallProviderRuntime,
            () => CanRequestProviderMaintenance);
        RequestUninstallProviderRuntimeAndCacheCommand = new RelayCommand(
            RequestUninstallProviderRuntimeAndCache,
            () => CanRequestProviderMaintenance);
        CancelProviderMaintenanceCommand = new RelayCommand(
            CancelProviderMaintenance,
            () => IsProviderMaintenanceConfirmationVisible);
        ConfirmProviderMaintenanceCommand = new AsyncRelayCommand(
            ConfirmProviderMaintenanceAsync,
            () => CanConfirmProviderMaintenance);
        StartProviderCommand = new AsyncRelayCommand(StartProviderAsync, () => CanStartProvider);
        StopProviderCommand = new AsyncRelayCommand(StopProviderAsync, () => CanStopProvider);
        SaveProviderConfigurationCommand = new AsyncRelayCommand(
            SaveProviderConfigurationAsync,
            () => CanSaveProviderConfiguration);
        SaveCurrentModelToLibraryCommand = new AsyncRelayCommand(
            SaveCurrentModelToLibraryAsync,
            () => CanSaveCurrentModelToLibrary);
        UseSelectedLibraryModelCommand = new RelayCommand(
            UseSelectedLibraryModel,
            () => CanUseSelectedLibraryModel);
        SaveGenerationProfileSelectionCommand = new AsyncRelayCommand(
            SaveGenerationProfileSelectionAsync,
            () => CanSaveGenerationProfileSelection);
        OpenGenerationDualSelectorCommand = new RelayCommand(
            OpenGenerationDualSelector,
            () => CanOpenGenerationDualSelector);
        ApplyGenerationDualSelectorCommand = new AsyncRelayCommand(
            ApplyGenerationDualSelectorAsync,
            () => CanApplyGenerationDualSelector);
        CancelGenerationDualSelectorCommand = new RelayCommand(
            CancelGenerationDualSelector,
            () => IsGenerationDualSelectorVisible);
        BeginCreateGenerationProfileCommand = new RelayCommand(
            BeginCreateGenerationProfile,
            () => CanCreateGenerationProfile);
        BeginEditGenerationProfileCommand = new RelayCommand(
            BeginEditGenerationProfile,
            () => CanEditSelectedGenerationProfile);
        SaveGenerationProfileDraftCommand = new AsyncRelayCommand(
            SaveGenerationProfileDraftAsync,
            () => CanSaveGenerationProfileDraft);
        CommitGenerationProfileCommand = new AsyncRelayCommand(
            CommitGenerationProfileAsync,
            () => CanCommitGenerationProfile);
        DiscardGenerationProfileDraftCommand = new AsyncRelayCommand(
            DiscardGenerationProfileDraftAsync,
            () => CanDiscardGenerationProfileDraft);
        CancelGenerationProfileEditCommand = new AsyncRelayCommand(
            CancelGenerationProfileEditAsync,
            () => IsGenerationProfileEditorVisible && !IsBusy && !IsGeneratingResponse);
        RestoreGenerationProfileRevisionCommand = new AsyncRelayCommand(
            RestoreGenerationProfileRevisionAsync,
            () => CanRestoreGenerationProfileRevision);
        UsePreviousGenerationProfileForSendCommand = new AsyncRelayCommand(
            UsePreviousGenerationProfileForSendAsync,
            () => CanUsePreviousGenerationProfileForSend);
        UseModifiedGenerationProfileForSendCommand = new AsyncRelayCommand(
            UseModifiedGenerationProfileForSendAsync,
            () => CanUseModifiedGenerationProfileForSend);
        CancelGenerationProfileSendGateCommand = new RelayCommand(
            CancelGenerationProfileSendGate,
            () => IsGenerationProfileSendGateVisible);
        ConfigurationGatePrimaryCommand = new AsyncRelayCommand(
            ExecuteConfigurationGatePrimaryActionAsync,
            () => ConfigurationGate.IsPrimaryActionEnabled);
    }

    public string ApplicationName { get; } = "Alicia";

    public ConversationWorkspaceViewModel ConversationWorkspace { get; }

    public ConversationHistoryViewModel ConversationHistory { get; }

    public ConversationStreamViewModel ConversationStream { get; }

    public ProviderViewModel Provider { get; }

    public ModelViewModel Model { get; }

    public GenerationDualSelectorViewModel GenerationDualSelector { get; }

    public GenerationSettingsViewModel GenerationSettings { get; }

    public ConversationConfigurationGateViewModel ConfigurationGate { get; }

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

    public IAsyncRelayCommand CheckProviderUpdateCommand { get; }

    public IAsyncRelayCommand UpdateProviderCommand { get; }

    public IAsyncRelayCommand InspectProviderStorageCommand { get; }

    public IRelayCommand RequestCleanupRetainedProviderReleasesCommand { get; }

    public IRelayCommand RequestUninstallProviderRuntimeCommand { get; }

    public IRelayCommand RequestUninstallProviderRuntimeAndCacheCommand { get; }

    public IRelayCommand CancelProviderMaintenanceCommand { get; }

    public IAsyncRelayCommand ConfirmProviderMaintenanceCommand { get; }

    public IAsyncRelayCommand StartProviderCommand { get; }

    public IAsyncRelayCommand StopProviderCommand { get; }

    public IAsyncRelayCommand SaveProviderConfigurationCommand { get; }

    public IAsyncRelayCommand SaveCurrentModelToLibraryCommand { get; }

    public IRelayCommand UseSelectedLibraryModelCommand { get; }

    public IAsyncRelayCommand SaveGenerationProfileSelectionCommand { get; }

    public IRelayCommand OpenGenerationDualSelectorCommand { get; }

    public IAsyncRelayCommand ApplyGenerationDualSelectorCommand { get; }

    public IRelayCommand CancelGenerationDualSelectorCommand { get; }

    public IRelayCommand BeginCreateGenerationProfileCommand { get; }

    public IRelayCommand BeginEditGenerationProfileCommand { get; }

    public IAsyncRelayCommand SaveGenerationProfileDraftCommand { get; }

    public IAsyncRelayCommand CommitGenerationProfileCommand { get; }

    public IAsyncRelayCommand DiscardGenerationProfileDraftCommand { get; }

    public IAsyncRelayCommand CancelGenerationProfileEditCommand { get; }

    public IAsyncRelayCommand RestoreGenerationProfileRevisionCommand { get; }

    public IAsyncRelayCommand UsePreviousGenerationProfileForSendCommand { get; }

    public IAsyncRelayCommand UseModifiedGenerationProfileForSendCommand { get; }

    public IRelayCommand CancelGenerationProfileSendGateCommand { get; }

    public IAsyncRelayCommand ConfigurationGatePrimaryCommand { get; }

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

    public ObservableCollection<InferenceModelLibraryItemViewModel> ModelLibraryItems =>
        Model.ModelLibraryItems;

    public InferenceModelLibraryItemViewModel? SelectedModelLibraryItem
    {
        get => Model.SelectedModelLibraryItem;
        set
        {
            if (ReferenceEquals(Model.SelectedModelLibraryItem, value))
            {
                return;
            }

            Model.SelectedModelLibraryItem = value;
            OnPropertyChanged();
            RaiseModelLibraryStateChanged();
        }
    }

    public string ModelLibraryStatusText => Model.ModelLibraryStatusText;

    public bool IsModelLibraryAvailable => Model.SupportsModelLibrary;

    public bool HasModelLibraryItems => Model.HasModelLibraryItems;

    public bool HasSelectedModelLibraryItem => Model.HasSelectedModelLibraryItem;

    public bool ShowModelLibraryUnavailableState => !IsModelLibraryAvailable;

    public bool ShowModelLibraryEmptyState => IsModelLibraryAvailable && !HasModelLibraryItems;

    public bool ShowModelLibrarySelectionPrompt => IsModelLibraryAvailable
        && HasModelLibraryItems
        && !HasSelectedModelLibraryItem;

    public bool ShowSelectedModelLibraryDetail => IsModelLibraryAvailable
        && HasSelectedModelLibraryItem;

    public bool HasSelectedModelLibraryProviderMismatch =>
        Model.HasSelectedModelLibraryProviderMismatch(SelectedProvider);

    public string SelectedModelLibraryDetailStatusText =>
        Model.GetSelectedModelLibraryDetailStatusText(SelectedProvider);

    public bool CanSaveCurrentModelToLibrary => !IsBusy
        && !IsProviderBusy
        && Model.CanSaveCurrentModelToLibrary(SelectedProvider);

    public bool CanUseSelectedLibraryModel => IsProviderSettingsEditable
        && Model.CanUseSelectedLibraryModel(SelectedProvider);

    public ObservableCollection<GenerationProfile> GenerationProfiles =>
        Model.GenerationProfiles;

    public bool IsGenerationDualSelectorVisible => GenerationDualSelector.IsOpen;

    private bool CanInteractWithGenerationDualSelector =>
        GenerationDualSelector.SupportsSelector
        && SelectedConversation is not null
        && _selectedConversationBranchId is not null
        && !IsBusy
        && !IsGeneratingResponse
        && !IsDeleteConfirmationVisible
        && !IsGenerationProfileSendGateVisible
        && !IsGenerationProfileEditorVisible;

    public bool CanOpenGenerationDualSelector =>
        CanInteractWithGenerationDualSelector
        && !IsGenerationDualSelectorVisible;

    public bool CanApplyGenerationDualSelector =>
        CanInteractWithGenerationDualSelector
        && IsGenerationDualSelectorVisible
        && GenerationDualSelector.HasPendingChanges
        && GenerationDualSelector.SelectedModel is not null
        && GenerationDualSelector.SelectedProfile is not null;

    public string GenerationDualSelectorLabel => GenerationDualSelector.TriggerLabel;

    public string GenerationDualSelectorStatusText => GenerationDualSelector.StatusText;

    public string GenerationDualSelectorReadinessText
    {
        get
        {
            ConversationGenerationSelection? selection = GenerationDualSelector.ResolvedSelection;
            if (selection is null)
            {
                return GenerationDualSelector.StatusText;
            }

            InferenceProviderConfiguration? saved = Model.SavedProviderConfiguration;
            if (saved is null
                || !saved.HasModelReference
                || !string.Equals(saved.ProviderId, selection.ModelScope.ProviderId, StringComparison.Ordinal)
                || !string.Equals(saved.ModelReference, selection.ModelScope.ModelReference, StringComparison.Ordinal))
            {
                return $"This branch expects '{selection.ModelScope.ModelReference}'. Open Models, reuse that library model, save its provider settings, then load it before sending.";
            }

            if (Provider.Snapshot is { State: InferenceProviderState.Running } snapshot
                && (!string.Equals(SelectedProvider?.Id, selection.ModelScope.ProviderId, StringComparison.Ordinal)
                    || !string.Equals(snapshot.ModelReference, selection.ModelScope.ModelReference, StringComparison.Ordinal)))
            {
                return $"This branch expects '{selection.ModelScope.ModelReference}', but another model is currently loaded. Stop the provider and load the branch model before sending.";
            }

            return GenerationDualSelector.StatusText;
        }
    }

    private bool IsGenerationDualSelectorReadyForSend
    {
        get
        {
            ConversationGenerationSelection? selection = GenerationDualSelector.ResolvedSelection;
            if (selection is null)
            {
                return true;
            }

            InferenceProviderConfiguration? saved = Model.SavedProviderConfiguration;
            if (saved is null
                || !saved.HasModelReference
                || !string.Equals(saved.ProviderId, selection.ModelScope.ProviderId, StringComparison.Ordinal)
                || !string.Equals(saved.ModelReference, selection.ModelScope.ModelReference, StringComparison.Ordinal))
            {
                return false;
            }

            return Provider.Snapshot is not { State: InferenceProviderState.Running } snapshot
                || string.Equals(SelectedProvider?.Id, selection.ModelScope.ProviderId, StringComparison.Ordinal)
                    && string.Equals(snapshot.ModelReference, selection.ModelScope.ModelReference, StringComparison.Ordinal);
        }
    }

    public GenerationProfile? SelectedGenerationProfile
    {
        get => Model.SelectedGenerationProfile;
        set
        {
            if (ReferenceEquals(Model.SelectedGenerationProfile, value))
            {
                return;
            }

            Model.SelectedGenerationProfile = value;
            OnPropertyChanged();
            RaiseGenerationProfileSelectionStateChanged();
        }
    }

    public GenerationProfileEditorViewModel GenerationProfileEditor => Model.ProfileEditor;

    public bool IsGenerationProfileEditorVisible => GenerationProfileEditor.IsVisible;

    public bool CanEditGenerationProfileDraft =>
        Model.SupportsGenerationProfileEditing
        && !HasProviderConfigurationChanges
        && Model.SavedProviderConfiguration?.HasModelReference == true
        && Model.GenerationProfileScope is not null;

    public bool CanManageGenerationProfiles =>
        CanEditGenerationProfileDraft
        && !IsBusy
        && !IsGeneratingResponse;

    public bool CanBrowseGenerationProfiles =>
        CanManageGenerationProfiles
        && !IsGenerationProfileEditorVisible;

    public bool CanCreateGenerationProfile =>
        CanManageGenerationProfiles
        && !IsGenerationProfileEditorVisible;

    public bool CanEditSelectedGenerationProfile =>
        CanManageGenerationProfiles
        && !IsGenerationProfileEditorVisible
        && SelectedGenerationProfile is { IsDefault: false };

    public bool CanSaveGenerationProfileDraft =>
        CanManageGenerationProfiles
        && IsGenerationProfileEditorVisible;

    public bool CanCommitGenerationProfile =>
        CanManageGenerationProfiles
        && IsGenerationProfileEditorVisible
        && !GenerationProfileEditor.IsDraftStale;

    public bool CanDiscardGenerationProfileDraft =>
        CanManageGenerationProfiles
        && IsGenerationProfileEditorVisible
        && GenerationProfileEditor.HasPersistedWorkingDraft;

    public bool CanRestoreGenerationProfileRevision =>
        CanManageGenerationProfiles
        && !IsGenerationProfileEditorVisible
        && Model.ProfileHistory.SelectedRevision is { IsCurrent: false }
        && !Model.ProfileHistory.HasWorkingDraft;

    public string GenerationProfileScopeText => HasProviderConfigurationChanges
        ? "Unsaved model settings"
        : Model.GenerationProfileScope is GenerationProfileModelScope scope
            ? $"{scope.ProviderId} • {scope.ModelReference}"
            : "No saved model scope";

    public string GenerationProfileWorkspaceSelectionTitle =>
        SelectedGenerationProfile?.Name ?? "No profile selected";

    public string GenerationProfileWorkspaceSelectionDescription => SelectedGenerationProfile switch
    {
        null => "Choose a confirmed profile from the list. Browsing and editing profiles do not require an active conversation.",
        { IsDefault: true } => "Default is read-only and keeps provider/model generation behavior unless a custom confirmed profile is explicitly selected for a branch.",
        { Name: string name } => $"'{name}' is a confirmed custom profile. Editing creates or continues a local WorkingDraft; Save revision appends immutable history.",
    };

    public bool IsGenerationProfileSelectionEditable =>
        Model.SupportsGenerationProfileSelection
        && SelectedConversation is not null
        && _selectedConversationBranchId is not null
        && !IsBusy
        && !IsGeneratingResponse
        && !HasProviderConfigurationChanges
        && Model.SavedProviderConfiguration?.HasModelReference == true
        && GenerationProfiles.Count > 0
        && !IsGenerationProfileEditorVisible;

    public bool CanSaveGenerationProfileSelection =>
        Model.SupportsGenerationProfileSelection
        && SelectedConversation is not null
        && _selectedConversationBranchId is ConversationBranchId branchId
        && !IsBusy
        && !IsGeneratingResponse
        && !HasProviderConfigurationChanges
        && Model.SavedProviderConfiguration?.HasModelReference == true
        && GenerationProfiles.Count > 0
        && SelectedGenerationProfile is not null
        && Model.HasGenerationProfileSelectionChanges(SelectedConversation.Id, branchId);

    public bool IsGenerationProfileSendGateVisible => _isGenerationProfileSendGateVisible;

    public bool CanUsePreviousGenerationProfileForSend =>
        IsGenerationProfileSendGateVisible
        && !IsBusy
        && _generationProfileSendConversationId is not null
        && _generationProfileSendBranchId is not null
        && !string.IsNullOrWhiteSpace(_generationProfileSendContent);

    public bool CanUseModifiedGenerationProfileForSend =>
        CanUsePreviousGenerationProfileForSend
        && !_generationProfileSendDraftIsStale;

    public string GenerationProfileSendGateTitle =>
        _generationProfileSendDraftIsStale
            ? "Profile draft needs attention"
            : "Use your modified profile?";

    public string GenerationProfileSendGateDescription
    {
        get
        {
            string profileName = _generationProfileSendProfileName ?? "the active profile";
            return _generationProfileSendDraftIsStale
                ? $"'{profileName}' has a local draft based on an older confirmed revision. Use the previous confirmed version for this message, or cancel and discard the stale draft before confirming it."
                : $"'{profileName}' has autosaved local changes that are not yet confirmed. Use the previous confirmed revision, or confirm the modified draft as a new immutable revision before sending.";
        }
    }

    public string GenerationProfileSelectionStatusText => HasProviderConfigurationChanges
        ? "Save the model/provider settings before changing the profile bound to this branch."
        : IsLegacyGenerationProfileFallbackPendingPin
            ? Model.GenerationProfileSelectionStatusText
            : CanSaveGenerationProfileSelection
                ? "Profile selection has unsaved changes for the active branch."
                : Model.GenerationProfileSelectionStatusText;

    private bool IsLegacyGenerationProfileFallbackPendingPin =>
        SelectedConversation is not null
        && SelectedGenerationProfile is not null
        && Model.GenerationProfileScope is GenerationProfileModelScope scope
        && Model.ResolvedGenerationSelection is
        {
            IsBranchScoped: false
        } resolved
        && resolved.ConversationId == SelectedConversation.Id
        && resolved.ModelScope == scope
        && resolved.ProfileId == SelectedGenerationProfile.Id;

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
            OnPropertyChanged(nameof(CanSaveCurrentModelToLibrary));
            OnPropertyChanged(nameof(CanUseSelectedLibraryModel));
            OnPropertyChanged(nameof(IsProviderSettingsEditable));
            OnPropertyChanged(nameof(IsProviderSelectionEditable));
            OnPropertyChanged(nameof(IsProviderReasoningBudgetEditable));
            OnPropertyChanged(nameof(IsGenerationProfileSelectionEditable));
            OnPropertyChanged(nameof(CanSaveGenerationProfileSelection));
            OnPropertyChanged(nameof(GenerationProfileSelectionStatusText));
            RaiseGenerationProfileEditorStateChanged();
            SendMessageCommand.NotifyCanExecuteChanged();
            RetryResponseCommand.NotifyCanExecuteChanged();
            SaveProviderConfigurationCommand.NotifyCanExecuteChanged();
            SaveCurrentModelToLibraryCommand.NotifyCanExecuteChanged();
            UseSelectedLibraryModelCommand.NotifyCanExecuteChanged();
            SaveGenerationProfileSelectionCommand.NotifyCanExecuteChanged();
            ConversationHistory.NotifyInteractionStateChanged();
        }
    }

    public bool IsInteractionEnabled => !IsBusy;

    public bool IsComposerEnabled => IsInteractionEnabled
        && HasSelectedConversation
        && !IsDeleteConfirmationVisible
        && !IsGenerationProfileSendGateVisible
        && !IsGenerationDualSelectorVisible
        && IsProviderRunning;

    public bool CanSendMessage => IsComposerEnabled
        && IsProviderRunning
        && IsGenerationDualSelectorReadyForSend
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
        && !IsGeneratingResponse
        && !IsProviderMaintenanceConfirmationVisible;

    public bool IsProviderSelectionEditable => IsProviderSettingsEditable;

    public bool CanDetectProvider => SelectedProvider is not null
        && !IsProviderBusy
        && !IsGeneratingResponse
        && !IsProviderMaintenanceConfirmationVisible;

    public bool CanInstallProvider => SelectedProvider is not null
        && !IsProviderBusy
        && !IsGeneratingResponse
        && !IsProviderMaintenanceConfirmationVisible
        && ProviderFailureKind is not InferenceProviderFailureKind.Network
            and not InferenceProviderFailureKind.Model
        && Provider.Snapshot?.State is InferenceProviderState.Missing
            or InferenceProviderState.Unsupported
            or InferenceProviderState.Faulted;

    public bool CanStartProvider => SelectedProvider is not null
        && !IsProviderBusy
        && !IsGeneratingResponse
        && !IsProviderMaintenanceConfirmationVisible
        && Provider.Snapshot?.State == InferenceProviderState.Ready
        && !HasProviderConfigurationChanges
        && Model.SavedProviderConfiguration?.HasModelReference == true
        && string.Equals(
            Model.PersistedSelectedProviderId,
            SelectedProvider.Id,
            StringComparison.Ordinal);

    public bool CanStopProvider => IsProviderRunning
        || Provider.Snapshot?.State == InferenceProviderState.Starting;

    public bool CanCheckProviderUpdate => SelectedProvider is not null
        && Provider.SupportsProviderUpdates
        && !IsProviderBusy
        && !IsGeneratingResponse
        && !IsProviderMaintenanceConfirmationVisible;

    public bool CanUpdateProvider => CanCheckProviderUpdate
        && !IsProviderRunning
        && Provider.IsProviderUpdateAvailable
        && Provider.Snapshot?.State == InferenceProviderState.Ready;

    public bool CanInspectProviderStorage => SelectedProvider is not null
        && Provider.SupportsProviderMaintenance
        && !IsProviderBusy
        && !IsGeneratingResponse
        && !IsProviderMaintenanceConfirmationVisible;

    public bool CanRequestProviderMaintenance => CanInspectProviderStorage
        && HasProviderStorageInfo
        && !IsProviderRunning;

    public bool CanCleanupRetainedProviderReleases => CanRequestProviderMaintenance
        && HasRetainedProviderReleases;

    public bool CanConfirmProviderMaintenance => IsProviderMaintenanceConfirmationVisible
        && !IsProviderBusy
        && !IsGeneratingResponse
        && !IsProviderRunning;

    public bool IsProviderMaintenanceConfirmationVisible =>
        _providerMaintenanceConfirmation != ProviderMaintenanceConfirmation.None;

    public string ProviderMaintenanceConfirmationTitle => _providerMaintenanceConfirmation switch
    {
        ProviderMaintenanceConfirmation.CleanupRetainedReleases => "Clean retained runtime releases?",
        ProviderMaintenanceConfirmation.UninstallRuntime => "Uninstall managed runtime?",
        ProviderMaintenanceConfirmation.UninstallRuntimeAndModelCache => "Uninstall runtime and model cache?",
        _ => string.Empty,
    };

    public string ProviderMaintenanceConfirmationDescription => _providerMaintenanceConfirmation switch
    {
        ProviderMaintenanceConfirmation.CleanupRetainedReleases =>
            "Only inactive Alicia-managed release directories are removed. The active managed release, model cache, provider configuration, and conversations are preserved.",
        ProviderMaintenanceConfirmation.UninstallRuntime =>
            "Managed llama.cpp binaries, activation metadata, stale build staging, and provider logs are removed. The local model cache, provider configuration, and conversations are preserved.",
        ProviderMaintenanceConfirmation.UninstallRuntimeAndModelCache =>
            "Managed llama.cpp runtime files and the local model cache are removed. Provider configuration and conversations are preserved. Cached model files must be downloaded again before reuse.",
        _ => string.Empty,
    };

    public string ProviderMaintenanceConfirmLabel => _providerMaintenanceConfirmation switch
    {
        ProviderMaintenanceConfirmation.CleanupRetainedReleases => "Clean releases",
        ProviderMaintenanceConfirmation.UninstallRuntime => "Uninstall runtime",
        ProviderMaintenanceConfirmation.UninstallRuntimeAndModelCache => "Uninstall runtime + cache",
        _ => string.Empty,
    };

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

    public bool HasProviderFailure => Provider.HasProviderFailure;

    public InferenceProviderFailureKind? ProviderFailureKind => Provider.ProviderFailureKind;

    public string ProviderFailureMessage => Provider.ProviderFailureMessage;

    public string ProviderVersionText => Provider.ProviderVersionText;

    public bool SupportsProviderUpdates => Provider.SupportsProviderUpdates;

    public bool IsProviderUpdateAvailable => Provider.IsProviderUpdateAvailable;

    public string ProviderInstalledReleaseText => Provider.ProviderInstalledReleaseText;

    public string ProviderValidatedReleaseText => Provider.ProviderValidatedReleaseText;

    public string ProviderLatestReleaseText => Provider.ProviderLatestReleaseText;

    public string ProviderUpdateStatusText => Provider.ProviderUpdateStatusText;

    public bool SupportsProviderMaintenance => Provider.SupportsProviderMaintenance;

    public bool HasProviderStorageInfo => Provider.HasProviderStorageInfo;

    public bool HasRetainedProviderReleases => Provider.HasRetainedProviderReleases;

    public string ProviderRuntimeStorageText => Provider.ProviderRuntimeStorageText;

    public string ProviderModelCacheStorageText => Provider.ProviderModelCacheStorageText;

    public string ProviderRetainedReleasesText => Provider.ProviderRetainedReleasesText;

    public string ProviderMaintenanceStatusText => Provider.ProviderMaintenanceStatusText;

    public bool SupportsProviderObservability => Provider.SupportsProviderObservability;

    public bool HasProviderGenerationObservation => Provider.HasProviderGenerationObservation;

    public string ProviderGenerationOutcomeText => Provider.ProviderGenerationOutcomeText;

    public string ProviderGenerationIdentityText => Provider.ProviderGenerationIdentityText;

    public string ProviderGenerationLatencyText => Provider.ProviderGenerationLatencyText;

    public string ProviderGenerationTokenUsageText => Provider.ProviderGenerationTokenUsageText;

    public string ProviderGenerationTimingText => Provider.ProviderGenerationTimingText;

    public string ProviderObservabilityPrivacyText => Provider.ProviderObservabilityPrivacyText;

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
            RaiseGenerationProfileSelectionStateChanged();
        }
    }

    public bool CanStopResponse => IsGeneratingResponse
        && _isStopResponseUnlocked
        && _responseCancellation is { IsCancellationRequested: false };

    public bool CanRetryResponse => !IsBusy
        && !IsGeneratingResponse
        && !IsGenerationDualSelectorVisible
        && IsGenerationDualSelectorReadyForSend
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
            UpdateConfigurationGate();
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
            RaiseGenerationDualSelectorStateChanged();
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
            OnPropertyChanged(nameof(ShowConversationHistoryPanel));
            OnPropertyChanged(nameof(ShowWideConversationHistoryPanel));
            OnPropertyChanged(nameof(ShowNarrowConversationHistoryPanel));
            OnPropertyChanged(nameof(ShowConversationHistoryReopenButton));
            OnPropertyChanged(nameof(ShowNarrowHistoryBackdrop));
        }
    }

    public bool IsConversationHistoryCollapsed => ConversationHistory.IsConversationHistoryCollapsed;

    public bool IsNarrowConversationLayout => _isNarrowConversationLayout;

    public bool ShowWideConversationHistoryPanel =>
        ShowConversationHistoryPanel && !IsNarrowConversationLayout;

    public bool ShowNarrowConversationHistoryPanel =>
        ShowConversationHistoryPanel && IsNarrowConversationLayout;

    public bool ShowNarrowHistoryBackdrop => ShowNarrowConversationHistoryPanel;

    public bool IsReducedMotionEnabled => ConversationStream.IsReducedMotionEnabled;

    public bool IsStandardMotionEnabled => !IsReducedMotionEnabled;

    public bool IsProviderProgressIndeterminateAnimationEnabled =>
        IsProviderProgressIndeterminate && IsStandardMotionEnabled;

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

    public bool ShowConfigurationOnboarding => IsInitialized
        && ConfigurationGate.IsVisible
        && !HasConversationHistory;

    public bool ShowInlineConfigurationGate => IsInitialized
        && ConfigurationGate.IsVisible
        && HasConversationHistory;

    public bool ShowReadyEmptyConversationState => IsHistoryEmpty
        && !ShowConfigurationOnboarding;

    public bool ShowMessageComposer => HasSelectedConversation
        && !ConfigurationGate.IsVisible;

    public bool ShowConversationHistoryPanel => IsConversationHistoryExpanded
        && !ShowConfigurationOnboarding;

    public bool ShowConversationHistoryReopenButton => IsConversationHistoryCollapsed
        && !ShowConfigurationOnboarding;

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
                    : !IsGenerationDualSelectorReadyForSend
                        ? GenerationDualSelectorReadinessText
                        : $"Enter sends • Shift+Enter adds a new line • {GenerationDualSelectorLabel}";

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
        _isNarrowHistoryOverlayOpen = false;
        OnPropertyChanged(nameof(IsNarrowConversationLayout));
        ApplyConversationHistoryExpansion();
        OnPropertyChanged(nameof(ShowWideConversationHistoryPanel));
        OnPropertyChanged(nameof(ShowNarrowConversationHistoryPanel));
        OnPropertyChanged(nameof(ShowNarrowHistoryBackdrop));
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
        _isNarrowHistoryOverlayOpen = false;
        ApplyConversationHistoryExpansion();
        RaiseConversationScrollStateChanged();
    }

    private void ApplyConversationHistoryExpansion()
    {
        IsConversationHistoryExpanded = _isNarrowConversationLayout
            ? _isNarrowHistoryOverlayOpen
            : _conversationUiState.IsConversationHistoryExpanded;
    }

    private void CloseNarrowHistoryOverlay()
    {
        if (!_isNarrowConversationLayout || !_isNarrowHistoryOverlayOpen)
        {
            return;
        }

        _isNarrowHistoryOverlayOpen = false;
        ApplyConversationHistoryExpansion();
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
        await RefreshGenerationProfilesAsync().ConfigureAwait(true);
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
            Provider.ClearFailure();
            await RefreshGenerationProfilesAsync().ConfigureAwait(true);
            RaiseProviderStateChanged();
            RaiseProviderConfigurationStateChanged();
        }).ConfigureAwait(true);
    }

    private async Task SaveCurrentModelToLibraryAsync()
    {
        if (!CanSaveCurrentModelToLibrary || SelectedProvider is null)
        {
            return;
        }

        await ExecuteOperationAsync(async () =>
        {
            await Model
                .SaveCurrentModelToLibraryAsync(SelectedProvider)
                .ConfigureAwait(true);
            await RefreshGenerationDualSelectorAsync().ConfigureAwait(true);
            OnPropertyChanged(nameof(ModelLibraryItems));
            OnPropertyChanged(nameof(SelectedModelLibraryItem));
            RaiseModelLibraryStateChanged();
        }).ConfigureAwait(true);
    }

    private void UseSelectedLibraryModel()
    {
        if (!CanUseSelectedLibraryModel || SelectedProvider is null)
        {
            return;
        }

        Model.UseSelectedLibraryModelAsDraft(SelectedProvider);
        RaiseProviderDraftChanged();
        RaiseModelLibraryStateChanged();
    }

    private async Task SaveGenerationProfileSelectionAsync()
    {
        if (!CanSaveGenerationProfileSelection
            || SelectedConversation is null
            || _selectedConversationBranchId is not ConversationBranchId branchId)
        {
            return;
        }

        await ExecuteOperationAsync(async () =>
        {
            await Model
                .SaveGenerationProfileSelectionAsync(
                    SelectedConversation.Id,
                    branchId)
                .ConfigureAwait(true);
            await RefreshGenerationDualSelectorAsync().ConfigureAwait(true);
            RaiseGenerationProfileSelectionStateChanged();
        }).ConfigureAwait(true);
    }

    private void OpenGenerationDualSelector()
    {
        if (!CanOpenGenerationDualSelector)
        {
            return;
        }

        GenerationDualSelector.Open();
        RaiseGenerationDualSelectorStateChanged();
    }

    private void CancelGenerationDualSelector()
    {
        if (!IsGenerationDualSelectorVisible)
        {
            return;
        }

        GenerationDualSelector.Cancel();
        RaiseGenerationDualSelectorStateChanged();
    }

    private async Task ApplyGenerationDualSelectorAsync()
    {
        if (!CanApplyGenerationDualSelector)
        {
            return;
        }

        await ExecuteOperationAsync(async () =>
        {
            await GenerationDualSelector.ApplyAsync().ConfigureAwait(true);
            await RefreshGenerationProfilesAsync().ConfigureAwait(true);
        }).ConfigureAwait(true);
    }

    private void BeginCreateGenerationProfile()
    {
        if (!CanCreateGenerationProfile)
        {
            return;
        }

        ExecuteWithGenerationProfileAutosaveSuppressed(Model.BeginCreateGenerationProfile);
        RaiseGenerationProfileEditorStateChanged();
        RaiseGenerationProfileSelectionStateChanged();
    }

    private void BeginEditGenerationProfile()
    {
        if (!CanEditSelectedGenerationProfile)
        {
            return;
        }

        ExecuteWithGenerationProfileAutosaveSuppressed(Model.BeginEditSelectedGenerationProfile);
        RaiseGenerationProfileEditorStateChanged();
        RaiseGenerationProfileSelectionStateChanged();
    }

    private async Task CancelGenerationProfileEditAsync()
    {
        if (!IsGenerationProfileEditorVisible || IsBusy || IsGeneratingResponse)
        {
            return;
        }

        bool autosaveSucceeded = await FlushGenerationProfileDraftAutosaveAsync().ConfigureAwait(true);
        if (!autosaveSucceeded && GenerationProfileEditor.HasUnpersistedChanges)
        {
            return;
        }

        ExecuteWithGenerationProfileAutosaveSuppressed(Model.CancelGenerationProfileEdit);
        RaiseGenerationProfileEditorStateChanged();
        RaiseGenerationProfileSelectionStateChanged();
    }

    private async Task SaveGenerationProfileDraftAsync()
    {
        if (!CanSaveGenerationProfileDraft)
        {
            return;
        }

        await CancelGenerationProfileDraftAutosaveAsync().ConfigureAwait(true);
        await ExecuteOperationAsync(async () =>
        {
            await ExecuteWithGenerationProfileAutosaveSuppressedAsync(
                () => Model.SaveGenerationProfileWorkingDraftAsync())
                .ConfigureAwait(true);
            RaiseGenerationProfileEditorStateChanged();
        }).ConfigureAwait(true);
    }

    private async Task CommitGenerationProfileAsync()
    {
        if (!CanCommitGenerationProfile)
        {
            return;
        }

        await CancelGenerationProfileDraftAutosaveAsync().ConfigureAwait(true);
        await ExecuteOperationAsync(async () =>
        {
            await ExecuteWithGenerationProfileAutosaveSuppressedAsync(
                () => Model.CommitGenerationProfileAsync())
                .ConfigureAwait(true);
            await RefreshGenerationDualSelectorAsync().ConfigureAwait(true);
            RaiseGenerationProfileSelectionStateChanged();
            RaiseGenerationProfileEditorStateChanged();
        }).ConfigureAwait(true);
    }

    private async Task DiscardGenerationProfileDraftAsync()
    {
        if (!CanDiscardGenerationProfileDraft)
        {
            return;
        }

        await CancelGenerationProfileDraftAutosaveAsync().ConfigureAwait(true);
        await ExecuteOperationAsync(async () =>
        {
            await ExecuteWithGenerationProfileAutosaveSuppressedAsync(
                () => Model.DiscardGenerationProfileWorkingDraftAsync())
                .ConfigureAwait(true);
            RaiseGenerationProfileSelectionStateChanged();
            RaiseGenerationProfileEditorStateChanged();
        }).ConfigureAwait(true);
    }

    private async Task RestoreGenerationProfileRevisionAsync()
    {
        if (!CanRestoreGenerationProfileRevision
            || Model.ProfileHistory.SelectedRevision is not
                GenerationProfileRevisionItemViewModel selectedRevision)
        {
            return;
        }

        await ExecuteOperationAsync(async () =>
        {
            await ExecuteWithGenerationProfileAutosaveSuppressedAsync(
                () => Model.RestoreSelectedGenerationProfileRevisionAsync(selectedRevision.Id))
                .ConfigureAwait(true);
            RaiseGenerationProfileSelectionStateChanged();
            RaiseGenerationProfileEditorStateChanged();
            RaiseGenerationProfileHistoryStateChanged();
        }).ConfigureAwait(true);
    }

    private void OnGenerationProfileHistoryPropertyChanged(
        object? sender,
        PropertyChangedEventArgs eventArgs)
    {
        _ = sender;
        _ = eventArgs;
        RaiseGenerationProfileHistoryStateChanged();
    }

    private void OnGenerationDualSelectorPropertyChanged(
        object? sender,
        PropertyChangedEventArgs eventArgs)
    {
        _ = sender;
        _ = eventArgs;
        RaiseGenerationDualSelectorStateChanged();
    }

    private void OnGenerationProfileEditorPropertyChanged(
        object? sender,
        PropertyChangedEventArgs eventArgs)
    {
        _ = sender;
        RaiseGenerationProfileEditorStateChanged();

        if (_suppressGenerationProfileDraftAutosave
            || !IsGenerationProfileEditableValueProperty(eventArgs.PropertyName)
            || !CanEditGenerationProfileDraft
            || !GenerationProfileEditor.IsVisible
            || !GenerationProfileEditor.HasUnpersistedChanges)
        {
            return;
        }

        ScheduleGenerationProfileDraftAutosave();
    }

    private static bool IsGenerationProfileEditableValueProperty(string? propertyName)
    {
        return propertyName is nameof(GenerationProfileEditorViewModel.Name)
            or nameof(GenerationProfileEditorViewModel.BaseSystemInstructions)
            or nameof(GenerationProfileEditorViewModel.MaxOutputTokensText)
            or nameof(GenerationProfileEditorViewModel.TemperatureText)
            or nameof(GenerationProfileEditorViewModel.TopPText)
            or nameof(GenerationProfileEditorViewModel.TopKText)
            or nameof(GenerationProfileEditorViewModel.SeedText)
            or nameof(GenerationProfileEditorViewModel.ReasoningModeIndex)
            or nameof(GenerationProfileEditorViewModel.ReasoningBudgetText)
            or nameof(GenerationProfileEditorViewModel.InitialSuggestionsText);
    }

    private void ScheduleGenerationProfileDraftAutosave()
    {
        _generationProfileDraftAutosaveCancellation?.Cancel();
        CancellationTokenSource cancellationSource = new();
        _generationProfileDraftAutosaveCancellation = cancellationSource;
        _generationProfileDraftAutosaveTask = PersistGenerationProfileDraftAfterDelayAsync(
            cancellationSource);
    }

    private async Task PersistGenerationProfileDraftAfterDelayAsync(
        CancellationTokenSource cancellationSource)
    {
        try
        {
            await Task
                .Delay(_generationProfileDraftAutosaveDelay, cancellationSource.Token)
                .ConfigureAwait(true);
            await PersistGenerationProfileDraftAutosaveAsync(cancellationSource.Token)
                .ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (cancellationSource.IsCancellationRequested)
        {
        }
        finally
        {
            if (ReferenceEquals(_generationProfileDraftAutosaveCancellation, cancellationSource))
            {
                _generationProfileDraftAutosaveCancellation = null;
            }

            cancellationSource.Dispose();
        }
    }

    private Task<bool> PersistGenerationProfileDraftAutosaveAsync(
        CancellationToken cancellationToken)
    {
        Task predecessor;
        TaskCompletionSource<bool> release = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        lock (_generationProfileDraftAutosaveQueueSync)
        {
            predecessor = _generationProfileDraftAutosaveQueueTail;
            _generationProfileDraftAutosaveQueueTail = release.Task;
        }

        return PersistGenerationProfileDraftAutosaveQueuedAsync(
            predecessor,
            release,
            cancellationToken);
    }

    private async Task<bool> PersistGenerationProfileDraftAutosaveQueuedAsync(
        Task predecessor,
        TaskCompletionSource<bool> release,
        CancellationToken cancellationToken)
    {
        try
        {
            await predecessor.ConfigureAwait(true);
            cancellationToken.ThrowIfCancellationRequested();
            return await PersistGenerationProfileDraftAutosaveCoreAsync(cancellationToken)
                .ConfigureAwait(true);
        }
        finally
        {
            release.TrySetResult(true);
        }
    }

    private async Task<bool> PersistGenerationProfileDraftAutosaveCoreAsync(
        CancellationToken cancellationToken)
    {
        int attemptedEditVersion = GenerationProfileEditor.EditVersion;
        try
        {
            if (!GenerationProfileEditor.IsVisible
                || !GenerationProfileEditor.HasUnpersistedChanges)
            {
                return true;
            }

            _suppressGenerationProfileDraftAutosave = true;
            try
            {
                await Model
                    .SaveGenerationProfileWorkingDraftAsync(cancellationToken)
                    .ConfigureAwait(true);
            }
            finally
            {
                _suppressGenerationProfileDraftAutosave = false;
            }

            if (IsGeneratingResponse && !GenerationProfileEditor.HasUnpersistedChanges)
            {
                GenerationProfileEditor.StatusText =
                    "Working draft autosaved locally. The active response continues with its already confirmed profile revision.";
            }

            RaiseGenerationProfileEditorStateChanged();
            return !GenerationProfileEditor.HasUnpersistedChanges;
        }
        catch (InvalidOperationException exception)
        {
            GenerationProfileEditor.StatusText = $"Draft not saved locally yet: {exception.Message}";
            RaiseGenerationProfileEditorStateChanged();
            return false;
        }
        catch (InvalidDataException exception)
        {
            GenerationProfileEditor.StatusText = $"Draft autosave failed: {exception.Message}";
            RaiseGenerationProfileEditorStateChanged();
            return false;
        }
        catch (IOException exception)
        {
            GenerationProfileEditor.StatusText = $"Draft autosave failed: {exception.Message}";
            RaiseGenerationProfileEditorStateChanged();
            return false;
        }
        catch (UnauthorizedAccessException exception)
        {
            GenerationProfileEditor.StatusText = $"Draft autosave failed: {exception.Message}";
            RaiseGenerationProfileEditorStateChanged();
            return false;
        }
        finally
        {
            if (GenerationProfileEditor.IsVisible
                && GenerationProfileEditor.HasUnpersistedChanges
                && GenerationProfileEditor.EditVersion != attemptedEditVersion
                && CanEditGenerationProfileDraft)
            {
                ScheduleGenerationProfileDraftAutosave();
            }
        }
    }

    private async Task<bool> FlushGenerationProfileDraftAutosaveAsync()
    {
        await CancelGenerationProfileDraftAutosaveAsync().ConfigureAwait(true);

        if (!GenerationProfileEditor.IsVisible
            || !GenerationProfileEditor.HasUnpersistedChanges)
        {
            return true;
        }

        return await PersistGenerationProfileDraftAutosaveAsync(CancellationToken.None)
            .ConfigureAwait(true);
    }

    private async Task CancelGenerationProfileDraftAutosaveAsync()
    {
        CancellationTokenSource? cancellationSource = _generationProfileDraftAutosaveCancellation;
        Task autosaveTask = _generationProfileDraftAutosaveTask;
        cancellationSource?.Cancel();

        try
        {
            await autosaveTask.ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (cancellationSource?.IsCancellationRequested == true)
        {
        }
    }

    private void ExecuteWithGenerationProfileAutosaveSuppressed(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);

        _suppressGenerationProfileDraftAutosave = true;
        try
        {
            action();
        }
        finally
        {
            _suppressGenerationProfileDraftAutosave = false;
        }
    }

    private async Task ExecuteWithGenerationProfileAutosaveSuppressedAsync(
        Func<Task> operation)
    {
        ArgumentNullException.ThrowIfNull(operation);

        _suppressGenerationProfileDraftAutosave = true;
        try
        {
            await operation().ConfigureAwait(true);
        }
        finally
        {
            _suppressGenerationProfileDraftAutosave = false;

            if (GenerationProfileEditor.IsVisible
                && GenerationProfileEditor.HasUnpersistedChanges
                && CanEditGenerationProfileDraft)
            {
                ScheduleGenerationProfileDraftAutosave();
            }
        }
    }

    private async Task RefreshGenerationProfilesAsync()
    {
        await Model
            .LoadGenerationProfilesAsync(
                SelectedConversation?.Id,
                _selectedConversationBranchId)
            .ConfigureAwait(true);
        await RefreshGenerationDualSelectorAsync().ConfigureAwait(true);
        RaiseGenerationProfileSelectionStateChanged();
    }

    private async Task RefreshGenerationDualSelectorAsync()
    {
        await GenerationDualSelector
            .LoadAsync(
                SelectedConversation?.Id,
                _selectedConversationBranchId,
                Model.SavedProviderConfiguration)
            .ConfigureAwait(true);
        RaiseGenerationDualSelectorStateChanged();
    }

    private void RaiseGenerationProfileSelectionStateChanged()
    {
        OnPropertyChanged(nameof(GenerationProfiles));
        OnPropertyChanged(nameof(SelectedGenerationProfile));
        OnPropertyChanged(nameof(GenerationProfileScopeText));
        OnPropertyChanged(nameof(GenerationProfileWorkspaceSelectionTitle));
        OnPropertyChanged(nameof(GenerationProfileWorkspaceSelectionDescription));
        OnPropertyChanged(nameof(CanBrowseGenerationProfiles));
        OnPropertyChanged(nameof(IsGenerationProfileSelectionEditable));
        OnPropertyChanged(nameof(CanSaveGenerationProfileSelection));
        OnPropertyChanged(nameof(GenerationProfileSelectionStatusText));
        SaveGenerationProfileSelectionCommand.NotifyCanExecuteChanged();
        RaiseGenerationProfileHistoryStateChanged();
        RaiseGenerationProfileEditorStateChanged();
    }

    private void RaiseGenerationProfileHistoryStateChanged()
    {
        OnPropertyChanged(nameof(CanRestoreGenerationProfileRevision));
        RestoreGenerationProfileRevisionCommand.NotifyCanExecuteChanged();
    }

    private void RaiseGenerationDualSelectorStateChanged()
    {
        OnPropertyChanged(nameof(IsGenerationDualSelectorVisible));
        OnPropertyChanged(nameof(CanOpenGenerationDualSelector));
        OnPropertyChanged(nameof(CanApplyGenerationDualSelector));
        OnPropertyChanged(nameof(GenerationDualSelectorLabel));
        OnPropertyChanged(nameof(GenerationDualSelectorStatusText));
        OnPropertyChanged(nameof(GenerationDualSelectorReadinessText));
        OnPropertyChanged(nameof(CanSendMessage));
        OnPropertyChanged(nameof(CanRetryResponse));
        OnPropertyChanged(nameof(ComposerStatusText));
        OpenGenerationDualSelectorCommand.NotifyCanExecuteChanged();
        ApplyGenerationDualSelectorCommand.NotifyCanExecuteChanged();
        CancelGenerationDualSelectorCommand.NotifyCanExecuteChanged();
        SendMessageCommand.NotifyCanExecuteChanged();
        RetryResponseCommand.NotifyCanExecuteChanged();
    }

    private void RaiseGenerationProfileEditorStateChanged()
    {
        OnPropertyChanged(nameof(GenerationProfileEditor));
        OnPropertyChanged(nameof(IsGenerationProfileEditorVisible));
        OnPropertyChanged(nameof(CanEditGenerationProfileDraft));
        OnPropertyChanged(nameof(CanManageGenerationProfiles));
        OnPropertyChanged(nameof(CanBrowseGenerationProfiles));
        OnPropertyChanged(nameof(CanCreateGenerationProfile));
        OnPropertyChanged(nameof(CanEditSelectedGenerationProfile));
        OnPropertyChanged(nameof(CanSaveGenerationProfileDraft));
        OnPropertyChanged(nameof(CanCommitGenerationProfile));
        OnPropertyChanged(nameof(CanDiscardGenerationProfileDraft));
        OnPropertyChanged(nameof(IsGenerationProfileSelectionEditable));
        OnPropertyChanged(nameof(CanSaveGenerationProfileSelection));
        BeginCreateGenerationProfileCommand.NotifyCanExecuteChanged();
        BeginEditGenerationProfileCommand.NotifyCanExecuteChanged();
        SaveGenerationProfileDraftCommand.NotifyCanExecuteChanged();
        CommitGenerationProfileCommand.NotifyCanExecuteChanged();
        DiscardGenerationProfileDraftCommand.NotifyCanExecuteChanged();
        CancelGenerationProfileEditCommand.NotifyCanExecuteChanged();
        RestoreGenerationProfileRevisionCommand.NotifyCanExecuteChanged();
        SaveGenerationProfileSelectionCommand.NotifyCanExecuteChanged();
        RaiseGenerationDualSelectorStateChanged();
    }

    private IInferenceProviderRuntime GetSelectedProviderRuntime()
    {
        return Provider.GetSelectedProviderRuntime();
    }

    private IInferenceProviderUpdateRuntime GetSelectedProviderUpdateRuntime()
    {
        return Provider.GetSelectedProviderUpdateRuntime();
    }

    private IInferenceProviderMaintenanceRuntime GetSelectedProviderMaintenanceRuntime()
    {
        return Provider.GetSelectedProviderMaintenanceRuntime();
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
        RaiseModelLibraryStateChanged();
        StartProviderCommand.NotifyCanExecuteChanged();
        RaiseGenerationProfileSelectionStateChanged();
        UpdateConfigurationGate();
    }

    private void RaiseModelLibraryStateChanged()
    {
        OnPropertyChanged(nameof(ModelLibraryStatusText));
        OnPropertyChanged(nameof(IsModelLibraryAvailable));
        OnPropertyChanged(nameof(HasModelLibraryItems));
        OnPropertyChanged(nameof(HasSelectedModelLibraryItem));
        OnPropertyChanged(nameof(ShowModelLibraryUnavailableState));
        OnPropertyChanged(nameof(ShowModelLibraryEmptyState));
        OnPropertyChanged(nameof(ShowModelLibrarySelectionPrompt));
        OnPropertyChanged(nameof(ShowSelectedModelLibraryDetail));
        OnPropertyChanged(nameof(HasSelectedModelLibraryProviderMismatch));
        OnPropertyChanged(nameof(SelectedModelLibraryDetailStatusText));
        OnPropertyChanged(nameof(CanSaveCurrentModelToLibrary));
        OnPropertyChanged(nameof(CanUseSelectedLibraryModel));
        SaveCurrentModelToLibraryCommand.NotifyCanExecuteChanged();
        UseSelectedLibraryModelCommand.NotifyCanExecuteChanged();
    }

    internal event EventHandler<WorkspaceNavigationRequestedEventArgs>? WorkspaceNavigationRequested;

    private async Task ExecuteConfigurationGatePrimaryActionAsync()
    {
        if (!ConfigurationGate.IsPrimaryActionEnabled)
        {
            return;
        }

        switch (ConfigurationGate.PrimaryAction)
        {
            case ConversationConfigurationGateAction.OpenProviders:
                WorkspaceNavigationRequested?.Invoke(
                    this,
                    new WorkspaceNavigationRequestedEventArgs(WorkspaceSection.Providers));
                break;
            case ConversationConfigurationGateAction.DetectProvider:
                await DetectProviderAsync().ConfigureAwait(true);
                break;
            case ConversationConfigurationGateAction.InstallProvider:
                await InstallProviderAsync().ConfigureAwait(true);
                break;
            case ConversationConfigurationGateAction.OpenModels:
                WorkspaceNavigationRequested?.Invoke(
                    this,
                    new WorkspaceNavigationRequestedEventArgs(WorkspaceSection.Models));
                break;
            case ConversationConfigurationGateAction.StartProvider:
                await StartProviderAsync().ConfigureAwait(true);
                break;
            case ConversationConfigurationGateAction.None:
            default:
                break;
        }
    }

    private void UpdateConfigurationGate()
    {
        if (!IsInitialized)
        {
            ConfigurationGate.Hide();
            RaiseConfigurationGateStateChanged();
            return;
        }

        InferenceProviderSnapshot? snapshot = Provider.Snapshot;

        if (ProviderOptions.Count == 0 || SelectedProvider is null)
        {
            ConfigureGate(
                ConversationConfigurationGateState.NoProvider,
                "Local AI setup required",
                "No inference provider is available. Open Providers to choose or install a local AI runtime.",
                ConversationConfigurationGateAction.OpenProviders,
                "Configure provider",
                isEnabled: true);
            return;
        }

        if (snapshot is null)
        {
            ConfigureGate(
                ConversationConfigurationGateState.ProviderNotDetected,
                $"Check {ProviderName}",
                "Alicia has not inspected the selected local AI runtime yet.",
                ConversationConfigurationGateAction.DetectProvider,
                "Check provider",
                CanDetectProvider);
            return;
        }

        switch (snapshot.State)
        {
            case InferenceProviderState.Detecting:
                ConfigureTransientGate(
                    ConversationConfigurationGateState.ProviderDetecting,
                    "Checking local AI",
                    ProviderDetailText,
                    "Checking…");
                return;
            case InferenceProviderState.Missing:
                ConfigureGate(
                    ConversationConfigurationGateState.ProviderMissing,
                    $"Install {ProviderName}",
                    "The selected local AI runtime is not installed. Conversation history stays available while Alicia prepares it.",
                    ConversationConfigurationGateAction.InstallProvider,
                    "Install provider",
                    CanInstallProvider);
                return;
            case InferenceProviderState.Installing:
                ConfigureTransientGate(
                    ConversationConfigurationGateState.ProviderInstalling,
                    "Installing local AI",
                    ProviderDetailText,
                    "Installing…");
                return;
            case InferenceProviderState.Unsupported:
                ConfigureGate(
                    ConversationConfigurationGateState.ProviderUnsupported,
                    "Provider unavailable on this system",
                    ProviderDetailText,
                    ConversationConfigurationGateAction.OpenProviders,
                    "Review provider",
                    isEnabled: true);
                return;
            case InferenceProviderState.Faulted:
                ConfigureProviderFailureGate();
                return;
            case InferenceProviderState.Ready:
                if (HasProviderFailure)
                {
                    ConfigureProviderFailureGate();
                    return;
                }

                ConfigureReadyProviderGate();
                return;
            case InferenceProviderState.Starting:
                ConfigureTransientGate(
                    ConversationConfigurationGateState.ProviderStarting,
                    "Loading local model",
                    ProviderDetailText,
                    "Loading…");
                return;
            case InferenceProviderState.Running:
                ConfigurationGate.Hide();
                RaiseConfigurationGateStateChanged();
                return;
            case InferenceProviderState.Stopping:
                ConfigureTransientGate(
                    ConversationConfigurationGateState.ProviderStopping,
                    "Stopping local AI",
                    ProviderDetailText,
                    "Stopping…");
                return;
            default:
                throw new InvalidOperationException(
                    $"Unsupported provider state '{snapshot.State}'.");
        }
    }

    private void ConfigureProviderFailureGate()
    {
        switch (ProviderFailureKind)
        {
            case InferenceProviderFailureKind.Model:
                ConfigureGate(
                    ConversationConfigurationGateState.ModelConfigurationRequired,
                    "Review model settings",
                    ProviderDetailText,
                    ConversationConfigurationGateAction.OpenModels,
                    "Review model",
                    isEnabled: true);
                return;
            case InferenceProviderFailureKind.Network:
                ConfigureGate(
                    ConversationConfigurationGateState.ProviderFaulted,
                    "Local AI connection problem",
                    ProviderDetailText,
                    ConversationConfigurationGateAction.DetectProvider,
                    "Check provider again",
                    CanDetectProvider);
                return;
            case InferenceProviderFailureKind.Missing:
                ConfigureGate(
                    ConversationConfigurationGateState.ProviderMissing,
                    $"Install {ProviderName}",
                    ProviderDetailText,
                    ConversationConfigurationGateAction.InstallProvider,
                    "Install provider",
                    CanInstallProvider);
                return;
            case InferenceProviderFailureKind.Unsupported:
                ConfigureGate(
                    ConversationConfigurationGateState.ProviderUnsupported,
                    "Provider unavailable on this system",
                    ProviderDetailText,
                    ConversationConfigurationGateAction.OpenProviders,
                    "Review provider",
                    isEnabled: true);
                return;
            case InferenceProviderFailureKind.Faulted:
            case null:
            default:
                ConfigureGate(
                    ConversationConfigurationGateState.ProviderFaulted,
                    "Provider needs attention",
                    ProviderDetailText,
                    ConversationConfigurationGateAction.OpenProviders,
                    "Review provider",
                    isEnabled: true);
                return;
        }
    }

    private void ConfigureReadyProviderGate()
    {
        InferenceProviderConfiguration? saved = Model.SavedProviderConfiguration;
        bool hasModelDraft = !string.IsNullOrWhiteSpace(ProviderModelReference);

        if (HasProviderConfigurationChanges
            && (saved?.HasModelReference == true || hasModelDraft))
        {
            ConfigureGate(
                ConversationConfigurationGateState.ModelConfigurationRequired,
                "Review model settings",
                "The model or generation settings contain unsaved changes. Review and save them before loading the model.",
                ConversationConfigurationGateAction.OpenModels,
                "Review model settings",
                isEnabled: true);
            return;
        }

        if (saved is null || !saved.HasModelReference)
        {
            ConfigureGate(
                ConversationConfigurationGateState.ModelConfigurationRequired,
                "Choose a local model",
                $"{ProviderName} is ready, but Alicia needs a saved model before local AI chat can start.",
                ConversationConfigurationGateAction.OpenModels,
                "Configure model",
                isEnabled: true);
            return;
        }

        ConfigureGate(
            ConversationConfigurationGateState.ProviderReadyToStart,
            "Load your local model",
            $"{ProviderName} is ready and configured for {saved.ModelReference}. Load it to enable local AI chat.",
            ConversationConfigurationGateAction.StartProvider,
            "Load model",
            CanStartProvider);
    }

    private void ConfigureTransientGate(
        ConversationConfigurationGateState state,
        string title,
        string detail,
        string actionLabel)
    {
        ConfigureGate(
            state,
            title,
            string.IsNullOrWhiteSpace(detail) ? "Alicia is updating the local AI runtime." : detail,
            ConversationConfigurationGateAction.None,
            actionLabel,
            isEnabled: false);
    }

    private void ConfigureGate(
        ConversationConfigurationGateState state,
        string title,
        string description,
        ConversationConfigurationGateAction action,
        string actionLabel,
        bool isEnabled)
    {
        ConfigurationGate.Configure(
            state,
            title,
            description,
            action,
            actionLabel,
            isEnabled);
        RaiseConfigurationGateStateChanged();
    }

    private void RaiseConfigurationGateStateChanged()
    {
        OnPropertyChanged(nameof(ShowConfigurationOnboarding));
        OnPropertyChanged(nameof(ShowInlineConfigurationGate));
        OnPropertyChanged(nameof(ShowReadyEmptyConversationState));
        OnPropertyChanged(nameof(ShowMessageComposer));
        OnPropertyChanged(nameof(ShowConversationHistoryPanel));
        OnPropertyChanged(nameof(ShowConversationHistoryReopenButton));
        OnPropertyChanged(nameof(IsComposerEnabled));
        OnPropertyChanged(nameof(CanSendMessage));
        ConfigurationGatePrimaryCommand.NotifyCanExecuteChanged();
        SendMessageCommand.NotifyCanExecuteChanged();
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
        Provider.InvalidateStorageInfo();
        RaiseProviderStateChanged();
        SerializedProgress<InferenceProviderProgress> progress = new(ApplyProviderProgress);

        await ExecuteProviderOperationAsync(
            InferenceProviderState.Installing,
            $"Preparing {ProviderName} installation…",
            async cancellationToken => await GetSelectedProviderRuntime()
                .InstallAsync(progress, cancellationToken)
                .ConfigureAwait(true)).ConfigureAwait(true);
    }

    private async Task CheckProviderUpdateAsync()
    {
        if (!CanCheckProviderUpdate)
        {
            return;
        }

        using CancellationTokenSource cancellationSource = new();
        _providerOperationCancellation = cancellationSource;
        IsProviderBusy = true;
        ClearError();

        try
        {
            InferenceProviderUpdateInfo updateInfo = await GetSelectedProviderUpdateRuntime()
                .CheckForUpdateAsync(cancellationSource.Token)
                .ConfigureAwait(true);
            Provider.ApplyUpdateInfo(updateInfo);
            RaiseProviderStateChanged();
        }
        catch (OperationCanceledException) when (cancellationSource.IsCancellationRequested)
        {
            ClearError();
        }
        catch (InferenceProviderException exception)
        {
            ErrorMessage = exception.UserMessage;
            Provider.SetUpdateStatusMessage(exception.UserMessage);
            RaiseProviderStateChanged();
        }
        catch (HttpRequestException)
        {
            SetProviderUpdateOperationFailure(
                "Alicia could not check the managed provider update. The active runtime was not changed.");
        }
        catch (InvalidDataException)
        {
            SetProviderUpdateOperationFailure(
                "Alicia could not check the managed provider update. The active runtime was not changed.");
        }
        catch (IOException)
        {
            SetProviderUpdateOperationFailure(
                "Alicia could not check the managed provider update. The active runtime was not changed.");
        }
        catch (UnauthorizedAccessException)
        {
            SetProviderUpdateOperationFailure(
                "Alicia could not check the managed provider update. The active runtime was not changed.");
        }
        catch (InvalidOperationException)
        {
            SetProviderUpdateOperationFailure(
                "Alicia could not check the managed provider update. The active runtime was not changed.");
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

    private async Task UpdateProviderAsync()
    {
        if (!CanUpdateProvider)
        {
            return;
        }

        ClearProviderProgress();
        Provider.InvalidateStorageInfo();
        RaiseProviderStateChanged();
        SerializedProgress<InferenceProviderProgress> progress = new(ApplyProviderProgress);
        using CancellationTokenSource cancellationSource = new();
        _providerOperationCancellation = cancellationSource;
        IsProviderBusy = true;
        ClearError();

        try
        {
            InferenceProviderUpdateResult result = await GetSelectedProviderUpdateRuntime()
                .UpdateAsync(progress, cancellationSource.Token)
                .ConfigureAwait(true);
            ApplyProviderSnapshot(result.Snapshot);
            Provider.ApplyUpdateInfo(result.UpdateInfo);
            ClearError();
            RaiseProviderStateChanged();
        }
        catch (OperationCanceledException) when (cancellationSource.IsCancellationRequested)
        {
            ClearProviderProgress();
            ClearError();
        }
        catch (InferenceProviderException exception)
        {
            ClearProviderProgress();
            ErrorMessage = exception.UserMessage;
            Provider.SetUpdateStatusMessage(exception.UserMessage);
            RaiseProviderStateChanged();
        }
        catch (HttpRequestException)
        {
            ClearProviderProgress();
            SetProviderUpdateOperationFailure(
                "Alicia could not complete the managed provider update. The previous release remains selected.");
        }
        catch (InvalidDataException)
        {
            ClearProviderProgress();
            SetProviderUpdateOperationFailure(
                "Alicia could not complete the managed provider update. The previous release remains selected.");
        }
        catch (IOException)
        {
            ClearProviderProgress();
            SetProviderUpdateOperationFailure(
                "Alicia could not complete the managed provider update. The previous release remains selected.");
        }
        catch (UnauthorizedAccessException)
        {
            ClearProviderProgress();
            SetProviderUpdateOperationFailure(
                "Alicia could not complete the managed provider update. The previous release remains selected.");
        }
        catch (InvalidOperationException)
        {
            ClearProviderProgress();
            SetProviderUpdateOperationFailure(
                "Alicia could not complete the managed provider update. The previous release remains selected.");
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

    private void SetProviderUpdateOperationFailure(string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        ErrorMessage = message;
        Provider.SetUpdateStatusMessage(message);
        RaiseProviderStateChanged();
    }

    private async Task InspectProviderStorageAsync()
    {
        if (!CanInspectProviderStorage)
        {
            return;
        }

        using CancellationTokenSource cancellationSource = new();
        _providerOperationCancellation = cancellationSource;
        IsProviderBusy = true;
        ClearError();

        try
        {
            InferenceProviderStorageInfo storageInfo = await GetSelectedProviderMaintenanceRuntime()
                .InspectStorageAsync(cancellationSource.Token)
                .ConfigureAwait(true);
            Provider.ApplyStorageInfo(storageInfo);
            RaiseProviderStateChanged();
        }
        catch (OperationCanceledException) when (cancellationSource.IsCancellationRequested)
        {
            ClearError();
        }
        catch (InferenceProviderException exception)
        {
            SetProviderMaintenanceOperationFailure(exception.UserMessage);
        }
        catch (IOException)
        {
            SetProviderMaintenanceOperationFailure(
                "Alicia could not inspect managed provider storage. No files were intentionally removed.");
        }
        catch (UnauthorizedAccessException)
        {
            SetProviderMaintenanceOperationFailure(
                "Alicia could not inspect managed provider storage. No files were intentionally removed.");
        }
        catch (InvalidOperationException)
        {
            SetProviderMaintenanceOperationFailure(
                "Alicia could not inspect managed provider storage. No files were intentionally removed.");
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

    private void RequestCleanupRetainedProviderReleases()
    {
        RequestProviderMaintenance(ProviderMaintenanceConfirmation.CleanupRetainedReleases);
    }

    private void RequestUninstallProviderRuntime()
    {
        RequestProviderMaintenance(ProviderMaintenanceConfirmation.UninstallRuntime);
    }

    private void RequestUninstallProviderRuntimeAndCache()
    {
        RequestProviderMaintenance(ProviderMaintenanceConfirmation.UninstallRuntimeAndModelCache);
    }

    private void RequestProviderMaintenance(ProviderMaintenanceConfirmation confirmation)
    {
        bool canRequest = confirmation == ProviderMaintenanceConfirmation.CleanupRetainedReleases
            ? CanCleanupRetainedProviderReleases
            : CanRequestProviderMaintenance;

        if (!canRequest || confirmation == ProviderMaintenanceConfirmation.None)
        {
            return;
        }

        _providerMaintenanceConfirmation = confirmation;
        ClearError();
        RaiseProviderMaintenanceConfirmationChanged();
    }

    private void CancelProviderMaintenance()
    {
        if (!IsProviderMaintenanceConfirmationVisible)
        {
            return;
        }

        _providerMaintenanceConfirmation = ProviderMaintenanceConfirmation.None;
        RaiseProviderMaintenanceConfirmationChanged();
    }

    private async Task ConfirmProviderMaintenanceAsync()
    {
        if (!CanConfirmProviderMaintenance)
        {
            return;
        }

        ProviderMaintenanceConfirmation confirmation = _providerMaintenanceConfirmation;
        _providerMaintenanceConfirmation = ProviderMaintenanceConfirmation.None;
        RaiseProviderMaintenanceConfirmationChanged();
        ClearProviderProgress();
        SerializedProgress<InferenceProviderProgress> progress = new(ApplyProviderProgress);
        using CancellationTokenSource cancellationSource = new();
        _providerOperationCancellation = cancellationSource;
        IsProviderBusy = true;
        ClearError();

        try
        {
            IInferenceProviderMaintenanceRuntime runtime = GetSelectedProviderMaintenanceRuntime();
            InferenceProviderMaintenanceResult result = confirmation switch
            {
                ProviderMaintenanceConfirmation.CleanupRetainedReleases =>
                    await runtime.CleanupRetainedReleasesAsync(progress, cancellationSource.Token)
                        .ConfigureAwait(true),
                ProviderMaintenanceConfirmation.UninstallRuntime =>
                    await runtime.UninstallAsync(
                            InferenceProviderRemovalMode.RuntimeOnly,
                            progress,
                            cancellationSource.Token)
                        .ConfigureAwait(true),
                ProviderMaintenanceConfirmation.UninstallRuntimeAndModelCache =>
                    await runtime.UninstallAsync(
                            InferenceProviderRemovalMode.RuntimeAndModelCache,
                            progress,
                            cancellationSource.Token)
                        .ConfigureAwait(true),
                _ => throw new InvalidOperationException(
                    "No provider maintenance action is awaiting confirmation."),
            };

            ApplyProviderSnapshot(result.Snapshot);
            Provider.ApplyMaintenanceResult(result);
            ClearError();
            RaiseProviderStateChanged();
        }
        catch (OperationCanceledException) when (cancellationSource.IsCancellationRequested)
        {
            ClearProviderProgress();
            ClearError();
        }
        catch (InferenceProviderException exception)
        {
            ClearProviderProgress();
            SetProviderMaintenanceOperationFailure(exception.UserMessage);
        }
        catch (IOException)
        {
            ClearProviderProgress();
            SetProviderMaintenanceOperationFailure(
                "Alicia could not complete the requested provider maintenance. Review file permissions and try again.");
        }
        catch (UnauthorizedAccessException)
        {
            ClearProviderProgress();
            SetProviderMaintenanceOperationFailure(
                "Alicia could not complete the requested provider maintenance. Review file permissions and try again.");
        }
        catch (InvalidOperationException)
        {
            ClearProviderProgress();
            SetProviderMaintenanceOperationFailure(
                "Alicia could not complete the requested provider maintenance. Review the provider state and try again.");
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

    private void SetProviderMaintenanceOperationFailure(string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        ErrorMessage = message;
        Provider.SetMaintenanceStatusMessage(message);
        RaiseProviderStateChanged();
    }

    private void RaiseProviderMaintenanceConfirmationChanged()
    {
        OnPropertyChanged(nameof(IsProviderMaintenanceConfirmationVisible));
        OnPropertyChanged(nameof(ProviderMaintenanceConfirmationTitle));
        OnPropertyChanged(nameof(ProviderMaintenanceConfirmationDescription));
        OnPropertyChanged(nameof(ProviderMaintenanceConfirmLabel));
        OnPropertyChanged(nameof(CanInspectProviderStorage));
        OnPropertyChanged(nameof(CanRequestProviderMaintenance));
        OnPropertyChanged(nameof(CanCleanupRetainedProviderReleases));
        OnPropertyChanged(nameof(CanConfirmProviderMaintenance));
        OnPropertyChanged(nameof(IsProviderSettingsEditable));
        OnPropertyChanged(nameof(IsProviderSelectionEditable));
        OnPropertyChanged(nameof(IsProviderModelEditable));
        OnPropertyChanged(nameof(IsProviderReasoningBudgetEditable));
        OnPropertyChanged(nameof(CanSaveProviderConfiguration));
        OnPropertyChanged(nameof(CanSaveCurrentModelToLibrary));
        OnPropertyChanged(nameof(CanUseSelectedLibraryModel));
        OnPropertyChanged(nameof(CanDetectProvider));
        OnPropertyChanged(nameof(CanInstallProvider));
        OnPropertyChanged(nameof(CanStartProvider));
        OnPropertyChanged(nameof(CanCheckProviderUpdate));
        OnPropertyChanged(nameof(CanUpdateProvider));
        InspectProviderStorageCommand.NotifyCanExecuteChanged();
        RequestCleanupRetainedProviderReleasesCommand.NotifyCanExecuteChanged();
        RequestUninstallProviderRuntimeCommand.NotifyCanExecuteChanged();
        RequestUninstallProviderRuntimeAndCacheCommand.NotifyCanExecuteChanged();
        CancelProviderMaintenanceCommand.NotifyCanExecuteChanged();
        ConfirmProviderMaintenanceCommand.NotifyCanExecuteChanged();
        DetectProviderCommand.NotifyCanExecuteChanged();
        InstallProviderCommand.NotifyCanExecuteChanged();
        StartProviderCommand.NotifyCanExecuteChanged();
        CheckProviderUpdateCommand.NotifyCanExecuteChanged();
        UpdateProviderCommand.NotifyCanExecuteChanged();
        SaveProviderConfigurationCommand.NotifyCanExecuteChanged();
        SaveCurrentModelToLibraryCommand.NotifyCanExecuteChanged();
        UseSelectedLibraryModelCommand.NotifyCanExecuteChanged();
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
        catch (InferenceProviderException exception)
        {
            SetProviderFault(
                exception.UserMessage,
                MapFailureState(exception.Kind),
                exception.Kind);
        }
        catch (InvalidOperationException)
        {
            SetProviderStopFault();
        }
        catch (IOException)
        {
            SetProviderStopFault();
        }
        catch (UnauthorizedAccessException)
        {
            SetProviderStopFault();
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
        catch (InferenceProviderException exception)
        {
            await HandleProviderFailureAsync(exception).ConfigureAwait(true);
        }
        catch (PlatformNotSupportedException exception)
        {
            await HandleProviderFailureAsync(new InferenceProviderException(
                InferenceProviderFailureKind.Unsupported,
                "This system does not support the selected local AI provider configuration. Review the provider requirements and try again.",
                exception)).ConfigureAwait(true);
        }
        catch (HttpRequestException exception)
        {
            await HandleProviderFailureAsync(new InferenceProviderException(
                InferenceProviderFailureKind.Network,
                "Alicia could not reach a service required by the local AI provider. Check the connection and try again.",
                exception)).ConfigureAwait(true);
        }
        catch (InvalidDataException exception)
        {
            await HandleLegacyProviderFailureAsync(exception).ConfigureAwait(true);
        }
        catch (IOException exception)
        {
            await HandleLegacyProviderFailureAsync(exception).ConfigureAwait(true);
        }
        catch (UnauthorizedAccessException exception)
        {
            await HandleLegacyProviderFailureAsync(exception).ConfigureAwait(true);
        }
        catch (ArgumentException exception)
        {
            await HandleLegacyProviderFailureAsync(exception).ConfigureAwait(true);
        }
        catch (InvalidOperationException exception)
        {
            await HandleLegacyProviderFailureAsync(exception).ConfigureAwait(true);
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

    private Task HandleLegacyProviderFailureAsync(Exception exception)
    {
        return HandleProviderFailureAsync(new InferenceProviderException(
            InferenceProviderFailureKind.Faulted,
            "The local AI provider could not complete the operation. Review the provider and try again.",
            exception));
    }

    private async Task HandleProviderFailureAsync(InferenceProviderException failure)
    {
        ErrorMessage = failure.UserMessage;

        if (failure.Kind == InferenceProviderFailureKind.Missing)
        {
            SetProviderFault(
                failure.UserMessage,
                InferenceProviderState.Missing,
                InferenceProviderFailureKind.Missing);
            return;
        }

        if (failure.Kind == InferenceProviderFailureKind.Unsupported)
        {
            SetProviderFault(
                failure.UserMessage,
                InferenceProviderState.Unsupported,
                InferenceProviderFailureKind.Unsupported);
            return;
        }

        await RestoreProviderAfterFailureAsync(failure).ConfigureAwait(true);
    }

    private async Task RestoreProviderAfterFailureAsync(InferenceProviderException failure)
    {
        try
        {
            InferenceProviderSnapshot snapshot = await GetSelectedProviderRuntime()
                .DetectAsync(CancellationToken.None)
                .ConfigureAwait(true);
            ApplyProviderSnapshot(snapshot);

            if (snapshot.State == InferenceProviderState.Ready)
            {
                Provider.SetLastFailure(failure.Kind, failure.UserMessage);
                RaiseProviderStateChanged();
            }
        }
        catch (InferenceProviderException detectionFailure)
        {
            SetProviderRedetectionFault(failure, detectionFailure);
        }
        catch (PlatformNotSupportedException exception)
        {
            SetProviderRedetectionFault(
                failure,
                new InferenceProviderException(
                    InferenceProviderFailureKind.Unsupported,
                    "Alicia could not verify the local AI provider on this system.",
                    exception));
        }
        catch (HttpRequestException exception)
        {
            SetProviderRedetectionFault(failure, CreateRedetectionFailure(exception));
        }
        catch (InvalidDataException exception)
        {
            SetProviderRedetectionFault(failure, CreateRedetectionFailure(exception));
        }
        catch (IOException exception)
        {
            SetProviderRedetectionFault(failure, CreateRedetectionFailure(exception));
        }
        catch (UnauthorizedAccessException exception)
        {
            SetProviderRedetectionFault(failure, CreateRedetectionFailure(exception));
        }
        catch (ArgumentException exception)
        {
            SetProviderRedetectionFault(failure, CreateRedetectionFailure(exception));
        }
        catch (InvalidOperationException exception)
        {
            SetProviderRedetectionFault(failure, CreateRedetectionFailure(exception));
        }
    }

    private static InferenceProviderException CreateRedetectionFailure(Exception exception)
    {
        return new InferenceProviderException(
            InferenceProviderFailureKind.Faulted,
            "Alicia could not verify the local AI provider after the operation failed.",
            exception);
    }

    private static InferenceProviderException CreateResponseRedetectionFailure(Exception exception)
    {
        return new InferenceProviderException(
            InferenceProviderFailureKind.Faulted,
            "Alicia could not verify the local AI provider after the response failed.",
            exception);
    }

    private void SetProviderRedetectionFault(
        InferenceProviderException originalFailure,
        InferenceProviderException detectionFailure)
    {
        bool detectionDefinesProviderAvailability = detectionFailure.Kind
            is InferenceProviderFailureKind.Missing
                or InferenceProviderFailureKind.Unsupported;
        InferenceProviderFailureKind failureKind = detectionDefinesProviderAvailability
            ? detectionFailure.Kind
            : originalFailure.Kind;
        string safeMessage = detectionDefinesProviderAvailability
            ? detectionFailure.UserMessage
            : $"{originalFailure.UserMessage} Alicia could not verify the provider afterward.";
        SetProviderFault(
            safeMessage,
            MapFailureState(failureKind),
            failureKind);
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
        OnPropertyChanged(nameof(IsProviderProgressIndeterminateAnimationEnabled));
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

    private void SetProviderStopFault()
    {
        SetProviderFault(
            "Alicia could not stop the local AI provider cleanly. Review the provider and try again.",
            InferenceProviderState.Faulted,
            InferenceProviderFailureKind.Faulted);
    }

    private static InferenceProviderState MapFailureState(
        InferenceProviderFailureKind failureKind)
    {
        return failureKind switch
        {
            InferenceProviderFailureKind.Missing => InferenceProviderState.Missing,
            InferenceProviderFailureKind.Unsupported => InferenceProviderState.Unsupported,
            _ => InferenceProviderState.Faulted,
        };
    }

    private void SetProviderFault(
        string detail,
        InferenceProviderState state,
        InferenceProviderFailureKind failureKind)
    {
        ErrorMessage = detail;
        Provider.SetFault(
            detail,
            state,
            ProviderModelReference,
            failureKind);
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

        if (!HasError)
        {
            CloseNarrowHistoryOverlay();
        }
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
        if (!CanSendMessage
            || SelectedConversation is null
            || _selectedConversationBranchId is not ConversationBranchId branchId)
        {
            return;
        }

        ConversationId conversationId = SelectedConversation.Id;
        string content = MessageDraft.Trim();
        ClearError();
        bool autosaveSucceeded = await FlushGenerationProfileDraftAutosaveAsync()
            .ConfigureAwait(true);

        if (!autosaveSucceeded
            && GenerationProfileEditor.HasUnpersistedChanges
            && IsOpenEditorForResolvedBranchProfile(conversationId, branchId))
        {
            ErrorMessage =
                "The active generation profile has local edits that could not be autosaved. Fix or discard those edits before sending.";
            return;
        }

        if (TryOpenGenerationProfileSendGate(conversationId, branchId, content))
        {
            return;
        }

        await ExecuteMessageSendAsync(
            conversationId,
            branchId,
            content,
            commitModifiedProfile: false)
            .ConfigureAwait(true);
    }

    private async Task UsePreviousGenerationProfileForSendAsync()
    {
        if (!TryTakePendingGenerationProfileSend(
            requireModifiedDraft: false,
            out ConversationId conversationId,
            out ConversationBranchId branchId,
            out string content))
        {
            return;
        }

        await ExecuteMessageSendAsync(
            conversationId,
            branchId,
            content,
            commitModifiedProfile: false)
            .ConfigureAwait(true);
    }

    private async Task UseModifiedGenerationProfileForSendAsync()
    {
        if (!TryTakePendingGenerationProfileSend(
            requireModifiedDraft: true,
            out ConversationId conversationId,
            out ConversationBranchId branchId,
            out string content))
        {
            return;
        }

        await ExecuteMessageSendAsync(
            conversationId,
            branchId,
            content,
            commitModifiedProfile: true)
            .ConfigureAwait(true);
    }

    private async Task ExecuteMessageSendAsync(
        ConversationId conversationId,
        ConversationBranchId branchId,
        string content,
        bool commitModifiedProfile)
    {
        if (SelectedConversation?.Id != conversationId
            || _selectedConversationBranchId != branchId
            || string.IsNullOrWhiteSpace(content))
        {
            return;
        }

        SetConversationScrollFollowing(conversationId, requestRestore: true);
        await PersistConversationUiStateAsync().ConfigureAwait(true);

        await ExecuteOperationAsync(async () =>
        {
            if (commitModifiedProfile)
            {
                await ExecuteWithGenerationProfileAutosaveSuppressedAsync(
                    () => Model.CommitResolvedGenerationProfileWorkingDraftAsync(
                        conversationId,
                        branchId))
                    .ConfigureAwait(true);
                RaiseGenerationProfileSelectionStateChanged();
                RaiseGenerationProfileEditorStateChanged();
            }

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

    private bool TryOpenGenerationProfileSendGate(
        ConversationId conversationId,
        ConversationBranchId branchId,
        string content)
    {
        if (!Model.TryGetResolvedGenerationProfileWorkingDraft(
            conversationId,
            branchId,
            out GenerationProfileWorkingDraft? workingDraft,
            out GenerationProfileRevision? latestRevision)
            || workingDraft is null
            || latestRevision is null)
        {
            return false;
        }

        _generationProfileSendConversationId = conversationId;
        _generationProfileSendBranchId = branchId;
        _generationProfileSendProfileId = workingDraft.ProfileId;
        _generationProfileSendContent = content;
        _generationProfileSendProfileName = latestRevision.Profile.Name;
        _generationProfileSendDraftIsStale = workingDraft.BaseRevisionId != latestRevision.Id;
        SetGenerationProfileSendGateVisible(true);
        return true;
    }

    private bool TryTakePendingGenerationProfileSend(
        bool requireModifiedDraft,
        out ConversationId conversationId,
        out ConversationBranchId branchId,
        out string content)
    {
        conversationId = default;
        branchId = default;
        content = string.Empty;

        bool canProceed = requireModifiedDraft
            ? CanUseModifiedGenerationProfileForSend
            : CanUsePreviousGenerationProfileForSend;
        if (!canProceed
            || _generationProfileSendConversationId is not ConversationId pendingConversationId
            || _generationProfileSendBranchId is not ConversationBranchId pendingBranchId
            || _generationProfileSendProfileId is not GenerationProfileId pendingProfileId
            || string.IsNullOrWhiteSpace(_generationProfileSendContent)
            || Model.ResolvedGenerationSelection?.ProfileId != pendingProfileId)
        {
            return false;
        }

        conversationId = pendingConversationId;
        branchId = pendingBranchId;
        content = _generationProfileSendContent;
        CloseGenerationProfileSendGate();
        return true;
    }

    private bool IsOpenEditorForResolvedBranchProfile(
        ConversationId conversationId,
        ConversationBranchId branchId)
    {
        ConversationGenerationSelection? selection = Model.ResolvedGenerationSelection;
        return GenerationProfileEditor.ProfileId is GenerationProfileId editorProfileId
            && selection is not null
            && selection.ConversationId == conversationId
            && selection.ProfileId == editorProfileId
            && (selection.BranchId is null || selection.BranchId == branchId);
    }

    private void CancelGenerationProfileSendGate()
    {
        CloseGenerationProfileSendGate();
    }

    private void CloseGenerationProfileSendGate()
    {
        _generationProfileSendConversationId = null;
        _generationProfileSendBranchId = null;
        _generationProfileSendProfileId = null;
        _generationProfileSendContent = null;
        _generationProfileSendProfileName = null;
        _generationProfileSendDraftIsStale = false;
        SetGenerationProfileSendGateVisible(false);
    }

    private void SetGenerationProfileSendGateVisible(bool value)
    {
        if (_isGenerationProfileSendGateVisible == value)
        {
            return;
        }

        _isGenerationProfileSendGateVisible = value;
        OnPropertyChanged(nameof(IsGenerationProfileSendGateVisible));
        OnPropertyChanged(nameof(CanUsePreviousGenerationProfileForSend));
        OnPropertyChanged(nameof(CanUseModifiedGenerationProfileForSend));
        OnPropertyChanged(nameof(GenerationProfileSendGateTitle));
        OnPropertyChanged(nameof(GenerationProfileSendGateDescription));
        OnPropertyChanged(nameof(CanSendMessage));
        OnPropertyChanged(nameof(IsComposerEnabled));
        SendMessageCommand.NotifyCanExecuteChanged();
        UsePreviousGenerationProfileForSendCommand.NotifyCanExecuteChanged();
        UseModifiedGenerationProfileForSendCommand.NotifyCanExecuteChanged();
        CancelGenerationProfileSendGateCommand.NotifyCanExecuteChanged();
        RaiseGenerationDualSelectorStateChanged();
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
        catch (InferenceProviderException exception)
        {
            RemoveStreamingMessage(streamingMessage);
            ScheduleRetryResponseUnlock();
            await ReconcileProviderAfterResponseFailureAsync(exception).ConfigureAwait(true);
            throw;
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
            Provider.RefreshObservability();
            RaiseProviderStateChanged();

            if (ReferenceEquals(_responseCancellation, cancellationSource))
            {
                _responseCancellation = null;
            }

            _isStopResponseUnlocked = false;
            OnPropertyChanged(nameof(CanStopResponse));
            StopResponseCommand.NotifyCanExecuteChanged();
        }
    }

    private async Task ReconcileProviderAfterResponseFailureAsync(
        InferenceProviderException failure)
    {
        if (failure.Kind is not InferenceProviderFailureKind.Network
            and not InferenceProviderFailureKind.Faulted)
        {
            return;
        }

        try
        {
            InferenceProviderSnapshot snapshot = await GetSelectedProviderRuntime()
                .DetectAsync(CancellationToken.None)
                .ConfigureAwait(true);
            ApplyProviderSnapshot(snapshot);

            if (snapshot.State != InferenceProviderState.Running)
            {
                Provider.SetLastFailure(failure.Kind, failure.UserMessage);
                RaiseProviderStateChanged();
            }
        }
        catch (InferenceProviderException detectionFailure)
        {
            SetProviderRedetectionFault(failure, detectionFailure);
        }
        catch (HttpRequestException exception)
        {
            SetProviderRedetectionFault(failure, CreateResponseRedetectionFailure(exception));
        }
        catch (InvalidDataException exception)
        {
            SetProviderRedetectionFault(failure, CreateResponseRedetectionFailure(exception));
        }
        catch (IOException exception)
        {
            SetProviderRedetectionFault(failure, CreateResponseRedetectionFailure(exception));
        }
        catch (UnauthorizedAccessException exception)
        {
            SetProviderRedetectionFault(failure, CreateResponseRedetectionFailure(exception));
        }
        catch (ArgumentException exception)
        {
            SetProviderRedetectionFault(failure, CreateResponseRedetectionFailure(exception));
        }
        catch (InvalidOperationException exception)
        {
            SetProviderRedetectionFault(failure, CreateResponseRedetectionFailure(exception));
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
        if (IsGenerationProfileSendGateVisible)
        {
            CancelGenerationProfileSendGate();
            return;
        }

        if (IsGenerationDualSelectorVisible)
        {
            CancelGenerationDualSelector();
            return;
        }

        if (IsDeleteConfirmationVisible)
        {
            CancelDelete();
            return;
        }

        if (IsProviderMaintenanceConfirmationVisible)
        {
            CancelProviderMaintenance();
            return;
        }

        if (ShowNarrowConversationHistoryPanel)
        {
            CloseNarrowHistoryOverlay();
            return;
        }

        CancelAllRenames();

        if (CanStopResponse)
        {
            StopResponse();
        }
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

        if (IsBusy)
        {
            return;
        }

        if (SelectedConversation?.Id == conversation.Id)
        {
            CloseNarrowHistoryOverlay();
            return;
        }

        await ExecuteOperationAsync(async () =>
        {
            await LoadConversationAsync(conversation).ConfigureAwait(true);
        }).ConfigureAwait(true);

        if (!HasError)
        {
            CloseNarrowHistoryOverlay();
        }
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
        CloseGenerationProfileSendGate();
        SelectedConversation = item;
        _selectedConversationBranchId = conversation.ActiveBranchId;
        ConversationStream.LoadMessages(conversation.Messages);
        await RefreshGenerationProfilesAsync().ConfigureAwait(true);

        IsDeleteConfirmationVisible = false;
        RaiseMessageStateChanged();
        RequestConversationScrollRestore();
    }

    private void ClearSelection()
    {
        CancelAllRenames();
        CloseGenerationProfileSendGate();
        SelectedConversation = null;
        _selectedConversationBranchId = null;
        Model.ClearConversationGenerationSelection();
        GenerationDualSelector.Clear();
        ConversationStream.ClearMessages();
        IsDeleteConfirmationVisible = false;
        RaiseGenerationProfileSelectionStateChanged();
        RaiseMessageStateChanged();
    }

    private void CancelAllRenames()
    {
        ConversationHistory.CancelAllRenames();
    }

    private async Task ToggleConversationHistoryAsync()
    {
        if (_isNarrowConversationLayout)
        {
            _isNarrowHistoryOverlayOpen = !IsConversationHistoryExpanded;
            ApplyConversationHistoryExpansion();
            return;
        }

        bool isExpanded = !IsConversationHistoryExpanded;
        _conversationUiState = _conversationUiState.WithHistoryExpanded(isExpanded);
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
        catch (InferenceProviderException exception)
        {
            ErrorMessage = exception.UserMessage;
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
        OnPropertyChanged(nameof(CanCheckProviderUpdate));
        OnPropertyChanged(nameof(CanUpdateProvider));
        OnPropertyChanged(nameof(IsProviderModelEditable));
        OnPropertyChanged(nameof(IsProviderSettingsEditable));
        OnPropertyChanged(nameof(IsProviderSelectionEditable));
        OnPropertyChanged(nameof(IsProviderReasoningBudgetEditable));
        OnPropertyChanged(nameof(CanSaveProviderConfiguration));
        OnPropertyChanged(nameof(CanSaveCurrentModelToLibrary));
        OnPropertyChanged(nameof(CanUseSelectedLibraryModel));
        OnPropertyChanged(nameof(HasProviderConfigurationChanges));
        OnPropertyChanged(nameof(ProviderConfigurationValidationText));
        OnPropertyChanged(nameof(HasProviderConfigurationValidationError));
        OnPropertyChanged(nameof(ProviderConfigurationStatusText));
        OnPropertyChanged(nameof(ProviderName));
        OnPropertyChanged(nameof(ProviderStatusText));
        OnPropertyChanged(nameof(ProviderDetailText));
        OnPropertyChanged(nameof(HasProviderFailure));
        OnPropertyChanged(nameof(ProviderFailureKind));
        OnPropertyChanged(nameof(ProviderFailureMessage));
        OnPropertyChanged(nameof(ProviderVersionText));
        OnPropertyChanged(nameof(SupportsProviderUpdates));
        OnPropertyChanged(nameof(IsProviderUpdateAvailable));
        OnPropertyChanged(nameof(ProviderInstalledReleaseText));
        OnPropertyChanged(nameof(ProviderValidatedReleaseText));
        OnPropertyChanged(nameof(ProviderLatestReleaseText));
        OnPropertyChanged(nameof(ProviderUpdateStatusText));
        OnPropertyChanged(nameof(SupportsProviderMaintenance));
        OnPropertyChanged(nameof(HasProviderStorageInfo));
        OnPropertyChanged(nameof(HasRetainedProviderReleases));
        OnPropertyChanged(nameof(ProviderRuntimeStorageText));
        OnPropertyChanged(nameof(ProviderModelCacheStorageText));
        OnPropertyChanged(nameof(ProviderRetainedReleasesText));
        OnPropertyChanged(nameof(ProviderMaintenanceStatusText));
        OnPropertyChanged(nameof(SupportsProviderObservability));
        OnPropertyChanged(nameof(HasProviderGenerationObservation));
        OnPropertyChanged(nameof(ProviderGenerationOutcomeText));
        OnPropertyChanged(nameof(ProviderGenerationIdentityText));
        OnPropertyChanged(nameof(ProviderGenerationLatencyText));
        OnPropertyChanged(nameof(ProviderGenerationTokenUsageText));
        OnPropertyChanged(nameof(ProviderGenerationTimingText));
        OnPropertyChanged(nameof(ProviderObservabilityPrivacyText));
        OnPropertyChanged(nameof(CanInspectProviderStorage));
        OnPropertyChanged(nameof(CanRequestProviderMaintenance));
        OnPropertyChanged(nameof(CanCleanupRetainedProviderReleases));
        OnPropertyChanged(nameof(CanConfirmProviderMaintenance));
        OnPropertyChanged(nameof(IsProviderProgressVisible));
        OnPropertyChanged(nameof(IsProviderProgressIndeterminate));
        OnPropertyChanged(nameof(IsProviderProgressIndeterminateAnimationEnabled));
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
        CheckProviderUpdateCommand.NotifyCanExecuteChanged();
        UpdateProviderCommand.NotifyCanExecuteChanged();
        InspectProviderStorageCommand.NotifyCanExecuteChanged();
        RequestCleanupRetainedProviderReleasesCommand.NotifyCanExecuteChanged();
        RequestUninstallProviderRuntimeCommand.NotifyCanExecuteChanged();
        RequestUninstallProviderRuntimeAndCacheCommand.NotifyCanExecuteChanged();
        CancelProviderMaintenanceCommand.NotifyCanExecuteChanged();
        ConfirmProviderMaintenanceCommand.NotifyCanExecuteChanged();
        StartProviderCommand.NotifyCanExecuteChanged();
        StopProviderCommand.NotifyCanExecuteChanged();
        SaveProviderConfigurationCommand.NotifyCanExecuteChanged();
        SaveCurrentModelToLibraryCommand.NotifyCanExecuteChanged();
        UseSelectedLibraryModelCommand.NotifyCanExecuteChanged();
        SendMessageCommand.NotifyCanExecuteChanged();
        RaiseGenerationDualSelectorStateChanged();
        UpdateConfigurationGate();
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
        RaiseGenerationDualSelectorStateChanged();
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
        OnPropertyChanged(nameof(ShowConfigurationOnboarding));
        OnPropertyChanged(nameof(ShowInlineConfigurationGate));
        OnPropertyChanged(nameof(ShowReadyEmptyConversationState));
        OnPropertyChanged(nameof(ShowMessageComposer));
        OnPropertyChanged(nameof(ShowConversationHistoryPanel));
        OnPropertyChanged(nameof(ShowWideConversationHistoryPanel));
        OnPropertyChanged(nameof(ShowNarrowConversationHistoryPanel));
        OnPropertyChanged(nameof(ShowConversationHistoryReopenButton));
        OnPropertyChanged(nameof(ShowNarrowHistoryBackdrop));
    }
}
