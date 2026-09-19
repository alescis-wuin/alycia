using System.Collections.ObjectModel;
using Alicia.Application.Generations;
using Alicia.Application.Providers;
using Alicia.Domain.Conversations;

namespace Alicia.Presentation.ViewModels;

public sealed class GenerationDualSelectorViewModel : ViewModelBase
{
    private readonly IInferenceModelLibraryStore? _modelLibraryStore;
    private readonly IGenerationProfileCatalogStore? _profileCatalogStore;
    private readonly IConversationBranchGenerationSelectionStore? _selectionStore;
    private readonly ObservableCollection<GenerationDualSelectorModelItemViewModel> _models = [];
    private readonly ObservableCollection<GenerationDualSelectorProfileItemViewModel> _profiles = [];
    private ConversationId? _conversationId;
    private ConversationBranchId? _branchId;
    private InferenceProviderConfiguration? _currentProviderConfiguration;
    private ConversationGenerationSelection? _resolvedSelection;
    private GenerationDualSelectorModelItemViewModel? _selectedModel;
    private GenerationDualSelectorProfileItemViewModel? _selectedProfile;
    private bool _isOpen;
    private string _statusText = "Select a conversation to choose a model and profile.";
    private string _triggerLabel = "Model | Profile";

    public GenerationDualSelectorViewModel(
        IInferenceModelLibraryStore? modelLibraryStore,
        IGenerationProfileCatalogStore? profileCatalogStore,
        IConversationBranchGenerationSelectionStore? selectionStore)
    {
        _modelLibraryStore = modelLibraryStore;
        _profileCatalogStore = profileCatalogStore;
        _selectionStore = selectionStore;
    }

    public ObservableCollection<GenerationDualSelectorModelItemViewModel> Models => _models;

    public ObservableCollection<GenerationDualSelectorProfileItemViewModel> Profiles => _profiles;

    public GenerationDualSelectorModelItemViewModel? SelectedModel
    {
        get => _selectedModel;
        set
        {
            if (!SetProperty(ref _selectedModel, value))
            {
                return;
            }

            ReplaceProfiles(value);
            OnPropertyChanged(nameof(HasPendingChanges));
            OnPropertyChanged(nameof(PreviewStatusText));
        }
    }

    public GenerationDualSelectorProfileItemViewModel? SelectedProfile
    {
        get => _selectedProfile;
        set
        {
            if (!SetProperty(ref _selectedProfile, value))
            {
                return;
            }

            OnPropertyChanged(nameof(HasPendingChanges));
            OnPropertyChanged(nameof(PreviewStatusText));
        }
    }

    public bool IsOpen
    {
        get => _isOpen;
        private set => SetProperty(ref _isOpen, value);
    }

    public bool SupportsSelector => _modelLibraryStore is not null
        && _profileCatalogStore is not null
        && _selectionStore is not null;

    public string TriggerLabel
    {
        get => _triggerLabel;
        private set => SetProperty(ref _triggerLabel, value);
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public string PreviewStatusText
    {
        get
        {
            if (SelectedModel is null || SelectedProfile is null)
            {
                return Models.Count == 0
                    ? "No saved model is available for this selector."
                    : "Choose both a model and a confirmed profile.";
            }

            string selectionText = $"{SelectedModel.DisplayName} | {SelectedProfile.Name}";
            if (!SelectedModel.IsCurrentSavedModel)
            {
                return $"{selectionText} will be pinned to this branch. Alicia will not switch or load the provider automatically; configure and load this model in Models before sending.";
            }

            return HasPendingChanges
                ? $"{selectionText} is ready to be pinned explicitly to this branch."
                : $"{selectionText} is already the effective branch selection.";
        }
    }

    public bool HasPendingChanges
    {
        get
        {
            if (_conversationId is not ConversationId conversationId
                || _branchId is not ConversationBranchId branchId
                || SelectedModel is null
                || SelectedProfile is null)
            {
                return false;
            }

            return _resolvedSelection is null
                || _resolvedSelection.ConversationId != conversationId
                || _resolvedSelection.BranchId != branchId
                || _resolvedSelection.ModelScope != SelectedModel.Scope
                || _resolvedSelection.ProfileId != SelectedProfile.Id;
        }
    }

    internal ConversationGenerationSelection? ResolvedSelection => _resolvedSelection;

    internal async Task LoadAsync(
        ConversationId? conversationId,
        ConversationBranchId? branchId,
        InferenceProviderConfiguration? currentProviderConfiguration,
        CancellationToken cancellationToken = default)
    {
        _conversationId = conversationId;
        _branchId = branchId;
        _currentProviderConfiguration = currentProviderConfiguration;
        _resolvedSelection = null;
        IsOpen = false;
        _models.Clear();
        _profiles.Clear();
        _selectedModel = null;
        _selectedProfile = null;
        OnPropertyChanged(nameof(SelectedModel));
        OnPropertyChanged(nameof(SelectedProfile));

        if (!SupportsSelector)
        {
            TriggerLabel = "Model | Profile";
            StatusText = "The model/profile selector is not available in this host.";
            RaiseSelectionDerivedStateChanged();
            return;
        }

        InferenceModelLibrary library = await _modelLibraryStore!
            .LoadAsync(cancellationToken)
            .ConfigureAwait(true)
            ?? InferenceModelLibrary.Empty;

        if (conversationId is ConversationId selectedConversationId
            && branchId is ConversationBranchId selectedBranchId)
        {
            _resolvedSelection = await _selectionStore!
                .LoadAsync(selectedConversationId, selectedBranchId, cancellationToken)
                .ConfigureAwait(true);
        }

        List<ModelSource> sources = [];
        foreach (InferenceModelLibraryEntry entry in library.Entries)
        {
            AddModelSource(
                sources,
                new GenerationProfileModelScope(entry.ProviderId, entry.ModelReference),
                isSavedInLibrary: true);
        }

        if (currentProviderConfiguration is { HasModelReference: true, ModelReference: not null })
        {
            AddModelSource(
                sources,
                new GenerationProfileModelScope(
                    currentProviderConfiguration.ProviderId,
                    currentProviderConfiguration.ModelReference),
                isSavedInLibrary: library.Find(
                    currentProviderConfiguration.ProviderId,
                    currentProviderConfiguration.ModelReference) is not null);
        }

        if (_resolvedSelection is ConversationGenerationSelection resolvedSelection)
        {
            AddModelSource(
                sources,
                resolvedSelection.ModelScope,
                isSavedInLibrary: library.Find(
                    resolvedSelection.ModelScope.ProviderId,
                    resolvedSelection.ModelScope.ModelReference) is not null);
        }

        foreach (ModelSource source in sources
            .OrderByDescending(source => _resolvedSelection is ConversationGenerationSelection selection
                && source.Scope == selection.ModelScope)
            .ThenByDescending(source => IsCurrentSavedModel(source.Scope))
            .ThenBy(source => source.Scope.ProviderId, StringComparer.Ordinal)
            .ThenBy(source => source.Scope.ModelReference, StringComparer.Ordinal))
        {
            GenerationProfileCatalog? persistedCatalog = await _profileCatalogStore!
                .LoadAsync(source.Scope, cancellationToken)
                .ConfigureAwait(true);
            GenerationProfileCatalog catalog = persistedCatalog
                ?? GenerationProfileCatalog.CreateEmpty(
                    source.Scope,
                    GenerationProfileId.New());

            _models.Add(new GenerationDualSelectorModelItemViewModel(
                source.Scope,
                catalog,
                isCatalogPersisted: persistedCatalog is not null,
                source.IsSavedInLibrary,
                IsCurrentSavedModel(source.Scope),
                isBoundToActiveBranch: _resolvedSelection?.ModelScope == source.Scope));
        }

        RestorePreviewSelection();
        RefreshEffectiveProjection();
        RaiseSelectionDerivedStateChanged();
    }

    internal void Clear()
    {
        _conversationId = null;
        _branchId = null;
        _currentProviderConfiguration = null;
        _resolvedSelection = null;
        IsOpen = false;
        _models.Clear();
        _profiles.Clear();
        _selectedModel = null;
        _selectedProfile = null;
        TriggerLabel = "Model | Profile";
        StatusText = "Select a conversation to choose a model and profile.";
        OnPropertyChanged(nameof(SelectedModel));
        OnPropertyChanged(nameof(SelectedProfile));
        RaiseSelectionDerivedStateChanged();
    }

    internal void Open()
    {
        RestorePreviewSelection();
        IsOpen = true;
        RaiseSelectionDerivedStateChanged();
    }

    internal void Cancel()
    {
        RestorePreviewSelection();
        IsOpen = false;
        RaiseSelectionDerivedStateChanged();
    }

    internal async Task ApplyAsync(CancellationToken cancellationToken = default)
    {
        if (!SupportsSelector
            || _conversationId is not ConversationId conversationId
            || _branchId is not ConversationBranchId branchId
            || SelectedModel is null
            || SelectedProfile is null)
        {
            throw new InvalidOperationException(
                "A conversation branch, model, and confirmed generation profile are required before applying a dual-selector choice.");
        }

        if (!SelectedModel.Catalog.Profiles.Any(profile => profile.Id == SelectedProfile.Id))
        {
            throw new InvalidOperationException(
                "The selected generation profile does not belong to the selected model scope.");
        }

        if (!SelectedModel.IsCatalogPersisted)
        {
            await _profileCatalogStore!
                .SaveAsync(SelectedModel.Catalog, cancellationToken)
                .ConfigureAwait(true);
            SelectedModel.MarkCatalogPersisted();
        }

        ConversationGenerationSelection selection = new(
            conversationId,
            branchId,
            SelectedModel.Scope,
            SelectedProfile.Id);
        await _selectionStore!
            .SaveAsync(selection, cancellationToken)
            .ConfigureAwait(true);

        _resolvedSelection = selection;
        foreach (GenerationDualSelectorModelItemViewModel model in _models)
        {
            model.SetBoundToActiveBranch(model.Scope == selection.ModelScope);
        }

        foreach (GenerationDualSelectorProfileItemViewModel profile in _profiles)
        {
            profile.SetBoundToActiveBranch(profile.Id == selection.ProfileId);
        }

        IsOpen = false;
        RefreshEffectiveProjection();
        RaiseSelectionDerivedStateChanged();
    }

    private void RestorePreviewSelection()
    {
        GenerationDualSelectorModelItemViewModel? model = null;

        if (_resolvedSelection is ConversationGenerationSelection selection)
        {
            model = _models.FirstOrDefault(item => item.Scope == selection.ModelScope);
        }

        if (model is null && _currentProviderConfiguration is { HasModelReference: true, ModelReference: not null })
        {
            GenerationProfileModelScope currentScope = new(
                _currentProviderConfiguration.ProviderId,
                _currentProviderConfiguration.ModelReference);
            model = _models.FirstOrDefault(item => item.Scope == currentScope);
        }

        model ??= _models.FirstOrDefault();
        SelectedModel = model;
    }

    private void ReplaceProfiles(GenerationDualSelectorModelItemViewModel? model)
    {
        _profiles.Clear();

        if (model is null)
        {
            SelectedProfile = null;
            return;
        }

        GenerationProfileId? resolvedProfileId = _resolvedSelection is ConversationGenerationSelection selection
            && selection.ModelScope == model.Scope
                ? selection.ProfileId
                : null;

        foreach (GenerationProfile profile in model.Catalog.Profiles)
        {
            _profiles.Add(new GenerationDualSelectorProfileItemViewModel(
                profile,
                hasWorkingDraft: model.Catalog.FindWorkingDraft(profile.Id) is not null,
                isBoundToActiveBranch: resolvedProfileId == profile.Id));
        }

        GenerationDualSelectorProfileItemViewModel? selected = resolvedProfileId is GenerationProfileId profileId
            ? _profiles.FirstOrDefault(profile => profile.Id == profileId)
            : _profiles.FirstOrDefault(profile => profile.IsDefault)
                ?? _profiles.FirstOrDefault();

        SelectedProfile = selected;
    }

    private void RefreshEffectiveProjection()
    {
        if (_resolvedSelection is ConversationGenerationSelection selection)
        {
            GenerationDualSelectorModelItemViewModel? model = _models.FirstOrDefault(item =>
                item.Scope == selection.ModelScope);
            GenerationProfile? profile = model?.Catalog.FindProfile(selection.ProfileId);
            string modelName = GenerationDualSelectorModelItemViewModel.FormatModelDisplayName(
                selection.ModelScope.ModelReference);
            string profileName = profile?.Name ?? "Unavailable profile";
            TriggerLabel = $"{modelName} | {profileName}";
            StatusText = selection.IsBranchScoped
                ? $"Current branch uses {modelName} | {profileName}."
                : $"Conversation fallback uses {modelName} | {profileName}. Apply the same pair to pin it explicitly to this branch.";
            return;
        }

        if (_currentProviderConfiguration is { HasModelReference: true, ModelReference: not null })
        {
            string modelName = GenerationDualSelectorModelItemViewModel.FormatModelDisplayName(
                _currentProviderConfiguration.ModelReference);
            TriggerLabel = $"{modelName} | Provider defaults";
            StatusText = "No model/profile pair is pinned to this branch. Generation still follows the legacy saved provider configuration until you apply a selector choice.";
            return;
        }

        TriggerLabel = "Choose model | profile";
        StatusText = _models.Count == 0
            ? "No model is available. Save a model in Models before binding a branch."
            : "No model/profile pair is pinned to this branch yet.";
    }

    private bool IsCurrentSavedModel(GenerationProfileModelScope scope)
    {
        return _currentProviderConfiguration is { HasModelReference: true, ModelReference: not null }
            && string.Equals(
                _currentProviderConfiguration.ProviderId,
                scope.ProviderId,
                StringComparison.Ordinal)
            && string.Equals(
                _currentProviderConfiguration.ModelReference,
                scope.ModelReference,
                StringComparison.Ordinal);
    }

    private static void AddModelSource(
        ICollection<ModelSource> sources,
        GenerationProfileModelScope scope,
        bool isSavedInLibrary)
    {
        ModelSource? existing = sources.FirstOrDefault(source => source.Scope == scope);
        if (existing is not null)
        {
            existing.IsSavedInLibrary |= isSavedInLibrary;
            return;
        }

        sources.Add(new ModelSource(scope, isSavedInLibrary));
    }

    private void RaiseSelectionDerivedStateChanged()
    {
        OnPropertyChanged(nameof(HasPendingChanges));
        OnPropertyChanged(nameof(PreviewStatusText));
    }

    private sealed class ModelSource
    {
        public ModelSource(GenerationProfileModelScope scope, bool isSavedInLibrary)
        {
            Scope = scope;
            IsSavedInLibrary = isSavedInLibrary;
        }

        public GenerationProfileModelScope Scope { get; }

        public bool IsSavedInLibrary { get; set; }
    }
}
