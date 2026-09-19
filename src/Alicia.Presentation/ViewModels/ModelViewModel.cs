using System.Collections.ObjectModel;
using Alicia.Application.Generations;
using Alicia.Application.Providers;
using Alicia.Domain.Conversations;

namespace Alicia.Presentation.ViewModels;

public sealed class ModelViewModel : ViewModelBase
{
    private readonly IInferenceProviderConfigurationStore _providerConfigurationStore;
    private readonly IGenerationProfileCatalogStore? _generationProfileCatalogStore;
    private readonly IConversationBranchGenerationSelectionStore? _conversationGenerationSelectionStore;
    private readonly TimeProvider _timeProvider;
    private readonly Dictionary<string, InferenceProviderConfiguration?> _providerConfigurations =
        new(StringComparer.Ordinal);
    private readonly ObservableCollection<GenerationProfile> _generationProfiles = [];
    private string _providerContextSizeText = string.Empty;
    private string _providerModelReference = string.Empty;
    private string? _persistedSelectedProviderId;
    private string? _providerSelectionNotice;
    private InferenceProviderConfiguration? _savedProviderConfiguration;
    private GenerationProfileCatalog? _generationProfileCatalog;
    private GenerationProfileModelScope? _generationProfileScope;
    private ConversationGenerationSelection? _resolvedGenerationSelection;
    private GenerationProfile? _selectedGenerationProfile;
    private bool _isGenerationProfileCatalogPersisted;
    private string _generationProfileSelectionStatusText =
        "Save a model reference to load generation profiles.";

    public ModelViewModel(
        IInferenceProviderConfigurationStore providerConfigurationStore,
        GenerationSettingsViewModel generationSettings,
        IGenerationProfileCatalogStore? generationProfileCatalogStore = null,
        IConversationBranchGenerationSelectionStore? conversationGenerationSelectionStore = null,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(providerConfigurationStore);
        ArgumentNullException.ThrowIfNull(generationSettings);

        _providerConfigurationStore = providerConfigurationStore;
        _generationProfileCatalogStore = generationProfileCatalogStore;
        _conversationGenerationSelectionStore = conversationGenerationSelectionStore;
        _timeProvider = timeProvider ?? TimeProvider.System;
        GenerationSettings = generationSettings;
        ProfileEditor = new GenerationProfileEditorViewModel();
    }

    public GenerationSettingsViewModel GenerationSettings { get; }

    public GenerationProfileEditorViewModel ProfileEditor { get; }

    public ObservableCollection<GenerationProfile> GenerationProfiles => _generationProfiles;

    public GenerationProfile? SelectedGenerationProfile
    {
        get => _selectedGenerationProfile;
        internal set => SetProperty(ref _selectedGenerationProfile, value);
    }

    public string ProviderModelReference
    {
        get => _providerModelReference;
        internal set => SetProperty(ref _providerModelReference, value);
    }

    public string ProviderContextSizeText
    {
        get => _providerContextSizeText;
        internal set => SetProperty(ref _providerContextSizeText, value);
    }

    internal string? PersistedSelectedProviderId => _persistedSelectedProviderId;

    internal string? ProviderSelectionNotice => _providerSelectionNotice;

    internal InferenceProviderConfiguration? SavedProviderConfiguration => _savedProviderConfiguration;

    internal bool SupportsGenerationProfileEditing =>
        _generationProfileCatalogStore is not null;

    internal bool SupportsGenerationProfileSelection =>
        SupportsGenerationProfileEditing
        && _conversationGenerationSelectionStore is not null;

    internal GenerationProfileModelScope? GenerationProfileScope => _generationProfileScope;

    internal ConversationGenerationSelection? ResolvedGenerationSelection =>
        _resolvedGenerationSelection;

    internal string GenerationProfileSelectionStatusText =>
        _generationProfileSelectionStatusText;

    internal async Task LoadAsync(IReadOnlyList<InferenceProviderDescriptor> providerOptions)
    {
        ArgumentNullException.ThrowIfNull(providerOptions);

        _providerConfigurations.Clear();

        foreach (InferenceProviderDescriptor descriptor in providerOptions)
        {
            InferenceProviderConfiguration? configuration = await _providerConfigurationStore
                .LoadAsync(descriptor.Id)
                .ConfigureAwait(true);
            _providerConfigurations[descriptor.Id] = configuration;
        }

        _persistedSelectedProviderId = await _providerConfigurationStore
            .LoadSelectedProviderIdAsync()
            .ConfigureAwait(true);
        _providerSelectionNotice = null;

        if (_persistedSelectedProviderId is not null
            && providerOptions.All(descriptor => !string.Equals(
                descriptor.Id,
                _persistedSelectedProviderId,
                StringComparison.Ordinal)))
        {
            _providerSelectionNotice =
                $"Configured provider '{_persistedSelectedProviderId}' is unavailable. Automatic fallback is disabled; choose and save an available provider.";
        }
    }

    internal void ApplySelectedProvider(InferenceProviderDescriptor? descriptor)
    {
        _savedProviderConfiguration = descriptor is not null
            && _providerConfigurations.TryGetValue(
                descriptor.Id,
                out InferenceProviderConfiguration? configuration)
            ? configuration
            : null;

        LoadProviderConfigurationDraft(_savedProviderConfiguration);
    }

    internal async Task<InferenceProviderConfiguration> SaveAsync(
        InferenceProviderDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);

        if (!TryBuildProviderConfiguration(descriptor, out InferenceProviderConfiguration? configuration, out _)
            || configuration is null)
        {
            throw new InvalidOperationException("Provider settings are invalid and cannot be saved.");
        }

        await _providerConfigurationStore
            .SaveAsync(configuration, selectProvider: true)
            .ConfigureAwait(true);

        _providerConfigurations[configuration.ProviderId] = configuration;
        _savedProviderConfiguration = configuration;
        _persistedSelectedProviderId = configuration.ProviderId;
        _providerSelectionNotice = null;
        return configuration;
    }

    internal bool HasConfigurationChanges(InferenceProviderDescriptor? descriptor)
    {
        if (descriptor is null)
        {
            return false;
        }

        if (!TryBuildProviderConfiguration(descriptor, out InferenceProviderConfiguration? draft, out _))
        {
            return true;
        }

        return !string.Equals(
                _persistedSelectedProviderId,
                descriptor.Id,
                StringComparison.Ordinal)
            || !Equals(_savedProviderConfiguration, draft);
    }

    internal bool TryBuildProviderConfiguration(
        InferenceProviderDescriptor? descriptor,
        out InferenceProviderConfiguration? configuration,
        out string? validationError)
    {
        configuration = null;
        validationError = null;

        if (descriptor is null)
        {
            validationError = "Select an inference provider.";
            return false;
        }

        if (!GenerationSettingsViewModel.TryParseOptionalInt(
            ProviderContextSizeText,
            "Context size",
            minimum: 1,
            out int? contextSize,
            out validationError))
        {
            return false;
        }

        if (!GenerationSettings.TryBuild(
            out InferenceGenerationOptions? generation,
            out validationError)
            || generation is null)
        {
            return false;
        }

        try
        {
            configuration = new InferenceProviderConfiguration(
                descriptor.Id,
                ProviderModelReference,
                contextSize,
                generation);
            return true;
        }
        catch (ArgumentException exception)
        {
            validationError = exception.Message;
            return false;
        }
    }

    internal string GetConfigurationStatusText(InferenceProviderDescriptor? descriptor)
    {
        return _providerSelectionNotice
            ?? (HasConfigurationChanges(descriptor)
                ? "Provider settings have unsaved changes."
                : _savedProviderConfiguration is null
                    ? "Save provider settings before starting a model."
                    : _savedProviderConfiguration.UsesProviderDefaults
                        ? "Saved • Optional runtime and generation values use provider defaults."
                        : "Saved • Explicit runtime or generation overrides are active.");
    }

    internal async Task LoadGenerationProfilesAsync(
        ConversationId? conversationId,
        ConversationBranchId? branchId,
        CancellationToken cancellationToken = default)
    {
        GenerationProfileId? profileEditorId = ProfileEditor.IsVisible
            ? ProfileEditor.ProfileId
            : null;
        ResetGenerationProfileProjection();

        try
        {
            if (!SupportsGenerationProfileEditing)
            {
                _generationProfileSelectionStatusText =
                    "Generation-profile editing and selection are not available in this host.";
                return;
            }

            if (_savedProviderConfiguration is null
                || !_savedProviderConfiguration.HasModelReference
                || _savedProviderConfiguration.ModelReference is null)
            {
                _generationProfileSelectionStatusText =
                    "Save a model reference to load generation profiles.";
                return;
            }

            GenerationProfileModelScope scope = new(
                _savedProviderConfiguration.ProviderId,
                _savedProviderConfiguration.ModelReference);
            _generationProfileScope = scope;

            GenerationProfileCatalog? persistedCatalog = await _generationProfileCatalogStore!
                .LoadAsync(scope, cancellationToken)
                .ConfigureAwait(true);
            _generationProfileCatalog = persistedCatalog
                ?? GenerationProfileCatalog.CreateEmpty(scope, GenerationProfileId.New());
            _isGenerationProfileCatalogPersisted = persistedCatalog is not null;
            ReplaceGenerationProfiles(_generationProfileCatalog.Profiles);

            if (_conversationGenerationSelectionStore is null)
            {
                _generationProfileSelectionStatusText =
                    "Generation profiles can be edited for this model, but branch profile selection is not available in this host.";
                return;
            }

            if (conversationId is null || branchId is null)
            {
                _generationProfileSelectionStatusText =
                    _isGenerationProfileCatalogPersisted
                        ? "Select a conversation to bind one of these profiles to its active branch."
                        : "No profile catalog is stored for this model yet. Select a conversation, choose Default, then save to create it explicitly.";
                return;
            }

            ConversationGenerationSelection? selection = await _conversationGenerationSelectionStore!
                .LoadAsync(conversationId.Value, branchId.Value, cancellationToken)
                .ConfigureAwait(true);
            _resolvedGenerationSelection = selection;

            if (selection is null)
            {
                _generationProfileSelectionStatusText = _isGenerationProfileCatalogPersisted
                    ? "No explicit profile is pinned to this branch. Generation still uses the legacy provider configuration until you save a profile selection."
                    : "No explicit profile is pinned to this branch and no profile catalog is stored for this model. Choose Default and save to create both explicitly.";
                return;
            }

            if (selection.ModelScope != scope)
            {
                _generationProfileSelectionStatusText =
                    $"This branch is pinned to model '{selection.ModelScope.ModelReference}'. Choose a profile below and save to rebind it to '{scope.ModelReference}'.";
                return;
            }

            GenerationProfile? selectedProfile = _generationProfileCatalog.FindProfile(selection.ProfileId);
            if (selectedProfile is null)
            {
                _generationProfileSelectionStatusText =
                    $"This branch references profile '{selection.ProfileId}', but that profile is unavailable for the saved model.";
                return;
            }

            SelectedGenerationProfile = selectedProfile;
            _generationProfileSelectionStatusText = selection.IsBranchScoped
                ? $"Current branch uses profile '{selectedProfile.Name}'."
                : $"Conversation fallback uses profile '{selectedProfile.Name}'. Save to pin it explicitly to the current branch.";
        }
        finally
        {
            RestoreGenerationProfileEditorAfterReload(profileEditorId);
        }
    }

    internal void ClearConversationGenerationSelection()
    {
        _resolvedGenerationSelection = null;
        SelectedGenerationProfile = null;

        if (_generationProfileScope is null)
        {
            return;
        }

        _generationProfileSelectionStatusText = _isGenerationProfileCatalogPersisted
            ? "Select a conversation to bind one of these profiles to its active branch."
            : "No profile catalog is stored for this model yet. Select a conversation, choose Default, then save to create it explicitly.";
    }

    internal bool HasGenerationProfileSelectionChanges(
        ConversationId conversationId,
        ConversationBranchId branchId)
    {
        if (!SupportsGenerationProfileSelection
            || _generationProfileScope is null
            || _generationProfileCatalog is null
            || SelectedGenerationProfile is null)
        {
            return false;
        }

        ConversationGenerationSelection? resolved = _resolvedGenerationSelection;
        return !_isGenerationProfileCatalogPersisted
            || resolved is null
            || resolved.BranchId != branchId
            || resolved.ConversationId != conversationId
            || resolved.ModelScope != _generationProfileScope.Value
            || resolved.ProfileId != SelectedGenerationProfile.Id;
    }

    internal async Task SaveGenerationProfileSelectionAsync(
        ConversationId conversationId,
        ConversationBranchId branchId,
        CancellationToken cancellationToken = default)
    {
        if (!SupportsGenerationProfileSelection
            || _generationProfileScope is null
            || _generationProfileCatalog is null
            || SelectedGenerationProfile is null)
        {
            throw new InvalidOperationException(
                "A saved model scope and selected generation profile are required before saving a branch selection.");
        }

        if (!_generationProfileCatalog.Profiles.Any(profile => profile.Id == SelectedGenerationProfile.Id))
        {
            throw new InvalidOperationException(
                "The selected generation profile does not belong to the loaded model scope.");
        }

        if (!_isGenerationProfileCatalogPersisted)
        {
            await _generationProfileCatalogStore!
                .SaveAsync(_generationProfileCatalog, cancellationToken)
                .ConfigureAwait(true);
            _isGenerationProfileCatalogPersisted = true;
        }

        ConversationGenerationSelection selection = new(
            conversationId,
            branchId,
            _generationProfileScope.Value,
            SelectedGenerationProfile.Id);
        await _conversationGenerationSelectionStore!
            .SaveAsync(selection, cancellationToken)
            .ConfigureAwait(true);

        _resolvedGenerationSelection = selection;
        _generationProfileSelectionStatusText =
            $"Current branch uses profile '{SelectedGenerationProfile.Name}'.";
    }

    internal bool TryGetResolvedGenerationProfileWorkingDraft(
        ConversationId conversationId,
        ConversationBranchId branchId,
        out GenerationProfileWorkingDraft? workingDraft,
        out GenerationProfileRevision? latestRevision)
    {
        workingDraft = null;
        latestRevision = null;

        if (_generationProfileCatalog is null
            || _generationProfileScope is null
            || _resolvedGenerationSelection is not ConversationGenerationSelection selection
            || selection.ConversationId != conversationId
            || selection.ModelScope != _generationProfileScope.Value
            || selection.BranchId is ConversationBranchId selectedBranchId
                && selectedBranchId != branchId)
        {
            return false;
        }

        workingDraft = _generationProfileCatalog.FindWorkingDraft(selection.ProfileId);
        if (workingDraft is null)
        {
            return false;
        }

        latestRevision = _generationProfileCatalog.FindLatestRevision(selection.ProfileId);
        return latestRevision is not null;
    }

    internal async Task<GenerationProfileRevision> CommitResolvedGenerationProfileWorkingDraftAsync(
        ConversationId conversationId,
        ConversationBranchId branchId,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetResolvedGenerationProfileWorkingDraft(
            conversationId,
            branchId,
            out GenerationProfileWorkingDraft? workingDraft,
            out GenerationProfileRevision? latestRevision)
            || workingDraft is null
            || latestRevision is null)
        {
            throw new InvalidOperationException(
                "The active branch does not have a persisted custom generation-profile draft to commit.");
        }

        if (workingDraft.BaseRevisionId != latestRevision.Id)
        {
            throw new InvalidOperationException(
                "The persisted generation-profile draft is stale relative to the latest confirmed revision.");
        }

        GenerationProfileCatalog catalog = RequireGenerationProfileCatalog();
        EnsureUniqueConfirmedProfileName(catalog, workingDraft.Profile);
        GenerationProfileCatalog updatedCatalog = catalog.CommitWorkingDraft(
            workingDraft.ProfileId,
            GenerationProfileRevisionId.New(),
            _timeProvider.GetUtcNow());

        await _generationProfileCatalogStore!
            .SaveAsync(updatedCatalog, cancellationToken)
            .ConfigureAwait(true);

        GenerationProfileId selectedProfileId = SelectedGenerationProfile?.Id
            ?? workingDraft.ProfileId;
        _generationProfileCatalog = updatedCatalog;
        _isGenerationProfileCatalogPersisted = true;
        ReplaceGenerationProfiles(updatedCatalog.Profiles);

        GenerationProfileRevision committedRevision = updatedCatalog
            .FindLatestRevision(workingDraft.ProfileId)
            ?? throw new InvalidOperationException(
                "The committed generation-profile revision could not be reloaded.");
        SelectedGenerationProfile = updatedCatalog.FindProfile(selectedProfileId)
            ?? committedRevision.Profile;

        if (ProfileEditor.IsVisible
            && ProfileEditor.ProfileId == committedRevision.ProfileId)
        {
            ProfileEditor.MarkCommitted(committedRevision);
        }

        return committedRevision;
    }

    internal void BeginCreateGenerationProfile()
    {
        if (_generationProfileCatalog is null || _generationProfileScope is null)
        {
            throw new InvalidOperationException(
                "A saved model scope is required before creating a generation profile.");
        }

        ProfileEditor.LoadNew(GenerationProfileId.New());
    }

    internal void BeginEditSelectedGenerationProfile()
    {
        if (_generationProfileCatalog is null
            || SelectedGenerationProfile is null
            || SelectedGenerationProfile.IsDefault)
        {
            throw new InvalidOperationException(
                "Select a confirmed custom generation profile before editing it.");
        }

        GenerationProfileRevision latestRevision = _generationProfileCatalog
            .FindLatestRevision(SelectedGenerationProfile.Id)
            ?? throw new InvalidOperationException(
                "The selected custom generation profile has no confirmed revision.");
        GenerationProfileWorkingDraft? draft = _generationProfileCatalog
            .FindWorkingDraft(SelectedGenerationProfile.Id);

        ProfileEditor.LoadExisting(
            latestRevision.Profile,
            latestRevision.Id,
            draft);
    }

    internal void CancelGenerationProfileEdit()
    {
        ProfileEditor.Close();
    }

    internal async Task SaveGenerationProfileWorkingDraftAsync(
        CancellationToken cancellationToken = default)
    {
        int editVersion = ProfileEditor.EditVersion;
        GenerationProfile profile = BuildEditorProfile();
        GenerationProfileCatalog catalog = RequireGenerationProfileCatalog();
        EnsureUniqueConfirmedProfileName(catalog, profile);

        GenerationProfileWorkingDraft draft = new(
            profile,
            ProfileEditor.BaseRevisionId,
            _timeProvider.GetUtcNow());
        GenerationProfileCatalog updatedCatalog = catalog.WithWorkingDraft(draft);

        await _generationProfileCatalogStore!
            .SaveAsync(updatedCatalog, cancellationToken)
            .ConfigureAwait(true);

        _generationProfileCatalog = updatedCatalog;
        _isGenerationProfileCatalogPersisted = true;
        ProfileEditor.MarkWorkingDraftSaved(draft, editVersion);
    }

    internal async Task CommitGenerationProfileAsync(
        CancellationToken cancellationToken = default)
    {
        if (ProfileEditor.IsDraftStale)
        {
            throw new InvalidOperationException(
                "The working draft is stale relative to the latest confirmed profile revision. Discard it before committing.");
        }

        GenerationProfile profile = BuildEditorProfile();
        GenerationProfileCatalog catalog = RequireGenerationProfileCatalog();
        EnsureUniqueConfirmedProfileName(catalog, profile);

        GenerationProfileWorkingDraft draft = new(
            profile,
            ProfileEditor.BaseRevisionId,
            _timeProvider.GetUtcNow());
        DateTimeOffset committedAt = _timeProvider.GetUtcNow();
        GenerationProfileCatalog updatedCatalog = catalog
            .WithWorkingDraft(draft)
            .CommitWorkingDraft(
                profile.Id,
                GenerationProfileRevisionId.New(),
                committedAt);

        await _generationProfileCatalogStore!
            .SaveAsync(updatedCatalog, cancellationToken)
            .ConfigureAwait(true);

        _generationProfileCatalog = updatedCatalog;
        _isGenerationProfileCatalogPersisted = true;
        ReplaceGenerationProfiles(updatedCatalog.Profiles);

        GenerationProfileRevision committedRevision = updatedCatalog
            .FindLatestRevision(profile.Id)
            ?? throw new InvalidOperationException(
                "The committed generation-profile revision could not be reloaded.");
        SelectedGenerationProfile = committedRevision.Profile;
        ProfileEditor.MarkCommitted(committedRevision);
    }

    internal async Task DiscardGenerationProfileWorkingDraftAsync(
        CancellationToken cancellationToken = default)
    {
        if (ProfileEditor.ProfileId is not GenerationProfileId profileId)
        {
            throw new InvalidOperationException(
                "No generation profile is currently open for editing.");
        }

        GenerationProfileCatalog catalog = RequireGenerationProfileCatalog();
        GenerationProfileWorkingDraft? draft = catalog.FindWorkingDraft(profileId);
        if (draft is not null)
        {
            GenerationProfileCatalog updatedCatalog = catalog.WithoutWorkingDraft(profileId);
            await _generationProfileCatalogStore!
                .SaveAsync(updatedCatalog, cancellationToken)
                .ConfigureAwait(true);
            _generationProfileCatalog = updatedCatalog;
            _isGenerationProfileCatalogPersisted = true;
            catalog = updatedCatalog;
        }

        GenerationProfileRevision? latestRevision = catalog.FindLatestRevision(profileId);
        if (latestRevision is null)
        {
            ProfileEditor.Close();
            return;
        }

        SelectedGenerationProfile = latestRevision.Profile;
        ProfileEditor.RestoreConfirmed(
            latestRevision.Profile,
            latestRevision.Id);
    }

    private GenerationProfile BuildEditorProfile()
    {
        if (!ProfileEditor.TryBuildProfile(
            out GenerationProfile? profile,
            out string? validationError)
            || profile is null)
        {
            throw new InvalidOperationException(
                validationError ?? "Generation-profile editor values are invalid.");
        }

        return profile;
    }

    private GenerationProfileCatalog RequireGenerationProfileCatalog()
    {
        if (_generationProfileCatalogStore is null
            || _generationProfileCatalog is null
            || _generationProfileScope is null)
        {
            throw new InvalidOperationException(
                "A saved model scope and generation-profile catalog are required.");
        }

        return _generationProfileCatalog;
    }

    private static void EnsureUniqueConfirmedProfileName(
        GenerationProfileCatalog catalog,
        GenerationProfile candidate)
    {
        bool duplicate = catalog.Profiles.Any(profile =>
            profile.Id != candidate.Id
            && string.Equals(
                profile.Name,
                candidate.Name,
                StringComparison.OrdinalIgnoreCase));
        if (duplicate)
        {
            throw new InvalidOperationException(
                $"A confirmed generation profile named '{candidate.Name}' already exists for this model.");
        }
    }

    private void RestoreGenerationProfileEditorAfterReload(GenerationProfileId? profileId)
    {
        if (profileId is not GenerationProfileId editorProfileId
            || _generationProfileCatalog is null)
        {
            return;
        }

        GenerationProfileRevision? latestRevision = _generationProfileCatalog
            .FindLatestRevision(editorProfileId);
        if (latestRevision is null)
        {
            return;
        }

        GenerationProfileWorkingDraft? workingDraft = _generationProfileCatalog
            .FindWorkingDraft(editorProfileId);
        ProfileEditor.LoadExisting(
            latestRevision.Profile,
            latestRevision.Id,
            workingDraft);
    }

    private void ResetGenerationProfileProjection()
    {
        ProfileEditor.Close();
        _generationProfiles.Clear();
        _generationProfileCatalog = null;
        _generationProfileScope = null;
        _resolvedGenerationSelection = null;
        _isGenerationProfileCatalogPersisted = false;
        SelectedGenerationProfile = null;
    }

    private void ReplaceGenerationProfiles(IEnumerable<GenerationProfile> profiles)
    {
        _generationProfiles.Clear();
        foreach (GenerationProfile profile in profiles)
        {
            _generationProfiles.Add(profile);
        }
    }

    private void LoadProviderConfigurationDraft(InferenceProviderConfiguration? configuration)
    {
        _providerModelReference = configuration?.ModelReference ?? string.Empty;
        _providerContextSizeText = GenerationSettingsViewModel.FormatOptional(configuration?.ContextSize);
        GenerationSettings.Load(configuration?.Generation);

        OnPropertyChanged(nameof(ProviderModelReference));
        OnPropertyChanged(nameof(ProviderContextSizeText));
    }
}
