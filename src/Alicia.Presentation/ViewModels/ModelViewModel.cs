using Alicia.Application.Providers;

namespace Alicia.Presentation.ViewModels;

public sealed class ModelViewModel : ViewModelBase
{
    private readonly IInferenceProviderConfigurationStore _providerConfigurationStore;
    private readonly Dictionary<string, InferenceProviderConfiguration?> _providerConfigurations =
        new(StringComparer.Ordinal);
    private string _providerContextSizeText = string.Empty;
    private string _providerModelReference = string.Empty;
    private string? _persistedSelectedProviderId;
    private string? _providerSelectionNotice;
    private InferenceProviderConfiguration? _savedProviderConfiguration;

    public ModelViewModel(
        IInferenceProviderConfigurationStore providerConfigurationStore,
        GenerationSettingsViewModel generationSettings)
    {
        ArgumentNullException.ThrowIfNull(providerConfigurationStore);
        ArgumentNullException.ThrowIfNull(generationSettings);

        _providerConfigurationStore = providerConfigurationStore;
        GenerationSettings = generationSettings;
    }

    public GenerationSettingsViewModel GenerationSettings { get; }

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

    private void LoadProviderConfigurationDraft(InferenceProviderConfiguration? configuration)
    {
        _providerModelReference = configuration?.ModelReference ?? string.Empty;
        _providerContextSizeText = GenerationSettingsViewModel.FormatOptional(configuration?.ContextSize);
        GenerationSettings.Load(configuration?.Generation);

        OnPropertyChanged(nameof(ProviderModelReference));
        OnPropertyChanged(nameof(ProviderContextSizeText));
    }
}
