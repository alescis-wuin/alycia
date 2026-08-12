using System.Collections.ObjectModel;
using Alicia.Application.Conversations;
using Alicia.Application.Providers;
using Alicia.Domain.Conversations;
using Alicia.Presentation.Threading;
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
    private readonly IInferenceProviderRegistry _providerRegistry;
    private readonly IInferenceProviderConfigurationStore _providerConfigurationStore;
    private readonly Dictionary<string, InferenceProviderConfiguration?> _providerConfigurations =
        new(StringComparer.Ordinal);

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
    private string _providerContextSizeText = string.Empty;
    private string _providerMaxOutputTokensText = string.Empty;
    private string _providerTemperatureText = string.Empty;
    private string _providerTopPText = string.Empty;
    private string _providerTopKText = string.Empty;
    private string _providerSeedText = string.Empty;
    private string? _persistedSelectedProviderId;
    private string? _providerSelectionNotice;
    private InferenceProviderConfiguration? _savedProviderConfiguration;
    private InferenceProviderDescriptor? _selectedProvider;
    private IInferenceProviderRuntime? _selectedProviderRuntime;
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
        IInferenceProviderRegistry providerRegistry,
        IInferenceProviderConfigurationStore providerConfigurationStore)
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

        _createConversation = createConversation;
        _appendMessage = appendMessage;
        _streamConversationTurn = streamConversationTurn;
        _loadConversation = loadConversation;
        _listConversations = listConversations;
        _renameConversation = renameConversation;
        _deleteConversation = deleteConversation;
        _providerRegistry = providerRegistry;
        _providerConfigurationStore = providerConfigurationStore;

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
        SaveProviderConfigurationCommand = new AsyncRelayCommand(
            SaveProviderConfigurationAsync,
            () => CanSaveProviderConfiguration);
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

    public IAsyncRelayCommand SaveProviderConfigurationCommand { get; }

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

    public IReadOnlyList<InferenceProviderDescriptor> ProviderOptions => _providerRegistry.Providers;

    public InferenceProviderDescriptor? SelectedProvider
    {
        get => _selectedProvider;
        set
        {
            if (_selectedProvider is not null
                && !IsProviderSelectionEditable
                && !Equals(_selectedProvider, value))
            {
                return;
            }

            if (SetProperty(ref _selectedProvider, value))
            {
                ApplySelectedProvider(value);
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
                RaiseProviderConfigurationStateChanged();
            }
        }
    }

    public string ProviderContextSizeText
    {
        get => _providerContextSizeText;
        set
        {
            if (SetProperty(ref _providerContextSizeText, value))
            {
                RaiseProviderConfigurationStateChanged();
            }
        }
    }

    public string ProviderMaxOutputTokensText
    {
        get => _providerMaxOutputTokensText;
        set
        {
            if (SetProperty(ref _providerMaxOutputTokensText, value))
            {
                RaiseProviderConfigurationStateChanged();
            }
        }
    }

    public string ProviderTemperatureText
    {
        get => _providerTemperatureText;
        set
        {
            if (SetProperty(ref _providerTemperatureText, value))
            {
                RaiseProviderConfigurationStateChanged();
            }
        }
    }

    public string ProviderTopPText
    {
        get => _providerTopPText;
        set
        {
            if (SetProperty(ref _providerTopPText, value))
            {
                RaiseProviderConfigurationStateChanged();
            }
        }
    }

    public string ProviderTopKText
    {
        get => _providerTopKText;
        set
        {
            if (SetProperty(ref _providerTopKText, value))
            {
                RaiseProviderConfigurationStateChanged();
            }
        }
    }

    public string ProviderSeedText
    {
        get => _providerSeedText;
        set
        {
            if (SetProperty(ref _providerSeedText, value))
            {
                RaiseProviderConfigurationStateChanged();
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
                OnPropertyChanged(nameof(CanSaveProviderConfiguration));
                OnPropertyChanged(nameof(IsProviderSettingsEditable));
                OnPropertyChanged(nameof(IsProviderSelectionEditable));
                SendMessageCommand.NotifyCanExecuteChanged();
                RetryResponseCommand.NotifyCanExecuteChanged();
                SaveProviderConfigurationCommand.NotifyCanExecuteChanged();

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
        && _providerSnapshot?.State is InferenceProviderState.Missing
            or InferenceProviderState.Unsupported
            or InferenceProviderState.Faulted;

    public bool CanStartProvider => SelectedProvider is not null
        && !IsProviderBusy
        && !IsGeneratingResponse
        && _providerSnapshot?.State == InferenceProviderState.Ready
        && !HasProviderConfigurationChanges
        && _savedProviderConfiguration?.HasModelReference == true
        && string.Equals(
            _persistedSelectedProviderId,
            SelectedProvider.Id,
            StringComparison.Ordinal);

    public bool CanStopProvider => IsProviderRunning
        || _providerSnapshot?.State == InferenceProviderState.Starting;

    public bool IsProviderModelEditable => IsProviderSettingsEditable;

    public bool HasProviderConfigurationChanges
    {
        get
        {
            if (SelectedProvider is null)
            {
                return false;
            }

            if (!TryBuildProviderConfiguration(out InferenceProviderConfiguration? draft, out _))
            {
                return true;
            }

            return !string.Equals(
                    _persistedSelectedProviderId,
                    SelectedProvider.Id,
                    StringComparison.Ordinal)
                || !Equals(_savedProviderConfiguration, draft);
        }
    }

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

    public string ProviderConfigurationStatusText => _providerSelectionNotice
        ?? (HasProviderConfigurationChanges
            ? "Provider settings have unsaved changes."
            : _savedProviderConfiguration is null
                ? "Save provider settings before starting a model."
                : _savedProviderConfiguration.UsesProviderDefaults
                    ? "Saved • Optional runtime and generation values use provider defaults."
                    : "Saved • Explicit runtime or generation overrides are active.");

    public string ProviderFallbackText => ProviderOptions.Count == 0
        ? "Fallback disabled • No inference provider is currently available."
        : "Fallback disabled • Alicia never switches inference providers automatically.";

    public string ProviderName => _providerSnapshot?.Name
        ?? SelectedProvider?.Name
        ?? "Local AI provider";

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
        _ => SelectedProvider is null ? "Not configured" : "Not detected",
    };

    public string ProviderDetailText => _providerSnapshot?.Detail
        ?? (SelectedProvider is null
            ? "Select an inference provider."
            : "Use Detect to inspect the selected inference provider runtime.");

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

    private async Task LoadProviderConfigurationAsync()
    {
        _providerConfigurations.Clear();

        foreach (InferenceProviderDescriptor descriptor in ProviderOptions)
        {
            InferenceProviderConfiguration? configuration = await _providerConfigurationStore
                .LoadAsync(descriptor.Id)
                .ConfigureAwait(true);
            _providerConfigurations[descriptor.Id] = configuration;
        }

        string? selectedProviderId = await _providerConfigurationStore
            .LoadSelectedProviderIdAsync()
            .ConfigureAwait(true);
        _persistedSelectedProviderId = selectedProviderId;
        _providerSelectionNotice = null;

        InferenceProviderDescriptor? selected = selectedProviderId is null
            ? null
            : ProviderOptions.FirstOrDefault(descriptor => string.Equals(
                descriptor.Id,
                selectedProviderId,
                StringComparison.Ordinal));

        if (selectedProviderId is not null && selected is null)
        {
            _providerSelectionNotice =
                $"Configured provider '{selectedProviderId}' is unavailable. Automatic fallback is disabled; choose and save an available provider.";
        }
        else if (selected is not null)
        {
            _providerRegistry.SelectProvider(selected.Id);
        }

        SelectedProvider = selected ?? (ProviderOptions.Count == 0 ? null : ProviderOptions[0]);
    }

    private void ApplySelectedProvider(InferenceProviderDescriptor? descriptor)
    {
        _selectedProviderRuntime = descriptor is null
            ? null
            : _providerRegistry.GetRequiredRuntime(descriptor.Id);
        _providerSnapshot = null;
        ClearProviderProgress();

        _savedProviderConfiguration = descriptor is not null
            && _providerConfigurations.TryGetValue(
                descriptor.Id,
                out InferenceProviderConfiguration? configuration)
            ? configuration
            : null;

        LoadProviderConfigurationDraft(_savedProviderConfiguration);
        RaiseProviderStateChanged();
    }

    private void LoadProviderConfigurationDraft(InferenceProviderConfiguration? configuration)
    {
        _providerModelReference = configuration?.ModelReference ?? string.Empty;
        _providerContextSizeText = FormatOptional(configuration?.ContextSize);
        _providerMaxOutputTokensText = FormatOptional(configuration?.Generation.MaxOutputTokens);
        _providerTemperatureText = FormatOptional(configuration?.Generation.Temperature);
        _providerTopPText = FormatOptional(configuration?.Generation.TopP);
        _providerTopKText = FormatOptional(configuration?.Generation.TopK);
        _providerSeedText = FormatOptional(configuration?.Generation.Seed);

        OnPropertyChanged(nameof(ProviderModelReference));
        OnPropertyChanged(nameof(ProviderContextSizeText));
        OnPropertyChanged(nameof(ProviderMaxOutputTokensText));
        OnPropertyChanged(nameof(ProviderTemperatureText));
        OnPropertyChanged(nameof(ProviderTopPText));
        OnPropertyChanged(nameof(ProviderTopKText));
        OnPropertyChanged(nameof(ProviderSeedText));
        RaiseProviderConfigurationStateChanged();
    }

    private async Task SaveProviderConfigurationAsync()
    {
        if (!CanSaveProviderConfiguration
            || !TryBuildProviderConfiguration(
                out InferenceProviderConfiguration? configuration,
                out _)
            || configuration is null)
        {
            return;
        }

        await ExecuteOperationAsync(async () =>
        {
            await _providerConfigurationStore
                .SaveAsync(configuration, selectProvider: true)
                .ConfigureAwait(true);

            _providerConfigurations[configuration.ProviderId] = configuration;
            _savedProviderConfiguration = configuration;
            _persistedSelectedProviderId = configuration.ProviderId;
            _providerSelectionNotice = null;
            _providerRegistry.SelectProvider(configuration.ProviderId);
            RaiseProviderConfigurationStateChanged();
        }).ConfigureAwait(true);
    }

    private IInferenceProviderRuntime GetSelectedProviderRuntime()
    {
        return _selectedProviderRuntime
            ?? throw new InvalidOperationException(
                "Select an inference provider before performing this operation.");
    }

    private bool TryBuildProviderConfiguration(
        out InferenceProviderConfiguration? configuration,
        out string? validationError)
    {
        configuration = null;
        validationError = null;

        if (SelectedProvider is null)
        {
            validationError = "Select an inference provider.";
            return false;
        }

        if (!TryParseOptionalInt(
            ProviderContextSizeText,
            "Context size",
            minimum: 1,
            out int? contextSize,
            out validationError))
        {
            return false;
        }

        if (!TryParseOptionalInt(
            ProviderMaxOutputTokensText,
            "Maximum output tokens",
            minimum: 1,
            out int? maxOutputTokens,
            out validationError))
        {
            return false;
        }

        if (!TryParseOptionalDouble(
            ProviderTemperatureText,
            "Temperature",
            minimum: 0,
            maximum: null,
            out double? temperature,
            out validationError))
        {
            return false;
        }

        if (!TryParseOptionalDouble(
            ProviderTopPText,
            "Top-p",
            minimum: 0,
            maximum: 1,
            out double? topP,
            out validationError))
        {
            return false;
        }

        if (!TryParseOptionalInt(
            ProviderTopKText,
            "Top-k",
            minimum: 0,
            out int? topK,
            out validationError))
        {
            return false;
        }

        if (!TryParseOptionalInt(
            ProviderSeedText,
            "Seed",
            minimum: 0,
            out int? seed,
            out validationError))
        {
            return false;
        }

        try
        {
            configuration = new InferenceProviderConfiguration(
                SelectedProvider.Id,
                ProviderModelReference,
                contextSize,
                new InferenceGenerationOptions(
                    maxOutputTokens,
                    temperature,
                    topP,
                    topK,
                    seed));
            return true;
        }
        catch (ArgumentException exception)
        {
            validationError = exception.Message;
            return false;
        }
    }

    private static bool TryParseOptionalInt(
        string value,
        string label,
        int minimum,
        out int? result,
        out string? validationError)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            result = null;
            validationError = null;
            return true;
        }

        if (!int.TryParse(
            value.Trim(),
            System.Globalization.NumberStyles.Integer,
            System.Globalization.CultureInfo.CurrentCulture,
            out int parsed))
        {
            result = null;
            validationError = $"{label} must be a whole number or left blank for the provider default.";
            return false;
        }

        if (parsed < minimum)
        {
            result = null;
            validationError = $"{label} must be at least {minimum} or left blank for the provider default.";
            return false;
        }

        result = parsed;
        validationError = null;
        return true;
    }

    private static bool TryParseOptionalDouble(
        string value,
        string label,
        double minimum,
        double? maximum,
        out double? result,
        out string? validationError)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            result = null;
            validationError = null;
            return true;
        }

        string trimmed = value.Trim();
        bool parsedSuccessfully = double.TryParse(
            trimmed,
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.CurrentCulture,
            out double parsed)
            || double.TryParse(
                trimmed,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out parsed);

        if (!parsedSuccessfully || double.IsNaN(parsed) || double.IsInfinity(parsed))
        {
            result = null;
            validationError = $"{label} must be a finite number or left blank for the provider default.";
            return false;
        }

        if (parsed < minimum || (maximum is double max && parsed > max))
        {
            result = null;
            validationError = maximum is double upperBound
                ? $"{label} must be between {minimum} and {upperBound} or left blank for the provider default."
                : $"{label} must be at least {minimum} or left blank for the provider default.";
            return false;
        }

        result = parsed;
        validationError = null;
        return true;
    }

    private static string FormatOptional(int? value)
    {
        return value?.ToString(System.Globalization.CultureInfo.InvariantCulture)
            ?? string.Empty;
    }

    private static string FormatOptional(double? value)
    {
        return value?.ToString("G", System.Globalization.CultureInfo.InvariantCulture)
            ?? string.Empty;
    }

    private void RaiseProviderConfigurationStateChanged()
    {
        OnPropertyChanged(nameof(HasProviderConfigurationChanges));
        OnPropertyChanged(nameof(CanSaveProviderConfiguration));
        OnPropertyChanged(nameof(ProviderConfigurationValidationText));
        OnPropertyChanged(nameof(HasProviderConfigurationValidationError));
        OnPropertyChanged(nameof(ProviderConfigurationStatusText));
        OnPropertyChanged(nameof(CanStartProvider));
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

        InferenceProviderConfiguration configuration = _savedProviderConfiguration
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

        RaiseProviderStateChanged();
    }

    private void SetProviderTransientState(
        InferenceProviderState state,
        string detail)
    {
        _providerSnapshot = new InferenceProviderSnapshot(
            _providerSnapshot?.Name ?? SelectedProvider?.Name ?? "Local AI provider",
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
            _providerSnapshot?.Name ?? SelectedProvider?.Name ?? "Local AI provider",
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
        OnPropertyChanged(nameof(IsProviderSettingsEditable));
        OnPropertyChanged(nameof(IsProviderSelectionEditable));
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
