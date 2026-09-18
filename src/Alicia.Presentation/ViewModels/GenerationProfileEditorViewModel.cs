using System.Globalization;
using Alicia.Application.Generations;
using Alicia.Application.Providers;

namespace Alicia.Presentation.ViewModels;

public sealed class GenerationProfileEditorViewModel : ViewModelBase
{
    private static readonly char[] _initialSuggestionSeparators = ['\r', '\n'];

    private GenerationProfileId? _profileId;
    private GenerationProfileRevisionId? _baseRevisionId;
    private GenerationProfileRevisionId? _latestRevisionId;
    private string _name = string.Empty;
    private string _baseSystemInstructions = string.Empty;
    private string _maxOutputTokensText = string.Empty;
    private string _temperatureText = string.Empty;
    private string _topPText = string.Empty;
    private string _topKText = string.Empty;
    private string _seedText = string.Empty;
    private int _reasoningModeIndex;
    private string _reasoningBudgetText = GenerationSettingsViewModel.DefaultReasoningBudgetTokens
        .ToString(CultureInfo.InvariantCulture);
    private string _initialSuggestionsText = string.Empty;
    private string _statusText = string.Empty;
    private bool _isVisible;
    private bool _hasPersistedWorkingDraft;

    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value);
    }

    public string BaseSystemInstructions
    {
        get => _baseSystemInstructions;
        set => SetProperty(ref _baseSystemInstructions, value);
    }

    public string MaxOutputTokensText
    {
        get => _maxOutputTokensText;
        set => SetProperty(ref _maxOutputTokensText, value);
    }

    public string TemperatureText
    {
        get => _temperatureText;
        set => SetProperty(ref _temperatureText, value);
    }

    public string TopPText
    {
        get => _topPText;
        set => SetProperty(ref _topPText, value);
    }

    public string TopKText
    {
        get => _topKText;
        set => SetProperty(ref _topKText, value);
    }

    public string SeedText
    {
        get => _seedText;
        set => SetProperty(ref _seedText, value);
    }

    public int ReasoningModeIndex
    {
        get => _reasoningModeIndex;
        set
        {
            if (value is < 0 or > 2)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    "Reasoning mode index must identify provider default, disabled, or enabled.");
            }

            if (SetProperty(ref _reasoningModeIndex, value))
            {
                OnPropertyChanged(nameof(IsReasoningBudgetEditable));
            }
        }
    }

    public string ReasoningBudgetText
    {
        get => _reasoningBudgetText;
        set => SetProperty(ref _reasoningBudgetText, value);
    }

    public string InitialSuggestionsText
    {
        get => _initialSuggestionsText;
        set => SetProperty(ref _initialSuggestionsText, value);
    }

    public string StatusText
    {
        get => _statusText;
        internal set => SetProperty(ref _statusText, value);
    }

    public bool IsVisible
    {
        get => _isVisible;
        private set => SetProperty(ref _isVisible, value);
    }

    public bool HasPersistedWorkingDraft
    {
        get => _hasPersistedWorkingDraft;
        private set => SetProperty(ref _hasPersistedWorkingDraft, value);
    }

    public bool IsReasoningBudgetEditable => ReasoningModeIndex == 2;

    public bool IsNewProfile => IsVisible
        && _profileId is not null
        && _latestRevisionId is null;

    public bool IsDraftStale => IsVisible
        && _baseRevisionId is GenerationProfileRevisionId baseRevisionId
        && _latestRevisionId is GenerationProfileRevisionId latestRevisionId
        && baseRevisionId != latestRevisionId;

    internal GenerationProfileId? ProfileId => _profileId;

    internal GenerationProfileRevisionId? BaseRevisionId => _baseRevisionId;

    internal void LoadNew(GenerationProfileId profileId)
    {
        if (profileId.IsEmpty)
        {
            throw new ArgumentException(
                "Generation-profile identifier cannot be empty.",
                nameof(profileId));
        }

        _profileId = profileId;
        _baseRevisionId = null;
        _latestRevisionId = null;
        LoadProfileFields(null);
        HasPersistedWorkingDraft = false;
        StatusText = "New profile draft. Save it locally or commit it as the first confirmed revision.";
        IsVisible = true;
        RaiseModeStateChanged();
    }

    internal void LoadExisting(
        GenerationProfile confirmedProfile,
        GenerationProfileRevisionId latestRevisionId,
        GenerationProfileWorkingDraft? workingDraft)
    {
        ArgumentNullException.ThrowIfNull(confirmedProfile);

        if (confirmedProfile.IsDefault)
        {
            throw new ArgumentException(
                "The built-in Default profile cannot be edited.",
                nameof(confirmedProfile));
        }

        if (latestRevisionId.IsEmpty)
        {
            throw new ArgumentException(
                "Latest generation-profile revision identifier cannot be empty.",
                nameof(latestRevisionId));
        }

        _profileId = confirmedProfile.Id;
        _latestRevisionId = latestRevisionId;
        _baseRevisionId = workingDraft?.BaseRevisionId ?? latestRevisionId;
        LoadProfileFields(workingDraft?.Profile ?? confirmedProfile);
        HasPersistedWorkingDraft = workingDraft is not null;
        IsVisible = true;
        StatusText = workingDraft is null
            ? $"Editing confirmed profile '{confirmedProfile.Name}'."
            : IsDraftStale
                ? "A local draft was restored, but it is stale relative to the latest confirmed revision. Discard it before committing a new revision."
                : "Local working draft restored.";
        RaiseModeStateChanged();
    }

    internal void MarkWorkingDraftSaved(GenerationProfileWorkingDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);

        if (_profileId != draft.ProfileId)
        {
            throw new InvalidOperationException(
                "The persisted working draft does not belong to the open profile editor.");
        }

        _baseRevisionId = draft.BaseRevisionId;
        LoadProfileFields(draft.Profile);
        HasPersistedWorkingDraft = true;
        StatusText = IsDraftStale
            ? "Working draft saved locally, but its base revision is stale. Discard it before committing."
            : "Working draft saved locally.";
        RaiseModeStateChanged();
    }

    internal void MarkCommitted(GenerationProfileRevision revision)
    {
        ArgumentNullException.ThrowIfNull(revision);

        if (_profileId != revision.ProfileId)
        {
            throw new InvalidOperationException(
                "The committed revision does not belong to the open profile editor.");
        }

        _baseRevisionId = revision.Id;
        _latestRevisionId = revision.Id;
        LoadProfileFields(revision.Profile);
        HasPersistedWorkingDraft = false;
        StatusText = $"Profile '{revision.Profile.Name}' saved as a new confirmed revision.";
        RaiseModeStateChanged();
    }

    internal void RestoreConfirmed(
        GenerationProfile confirmedProfile,
        GenerationProfileRevisionId latestRevisionId)
    {
        LoadExisting(confirmedProfile, latestRevisionId, workingDraft: null);
        StatusText = "Working draft discarded. Confirmed profile restored.";
    }

    internal void Close()
    {
        _profileId = null;
        _baseRevisionId = null;
        _latestRevisionId = null;
        HasPersistedWorkingDraft = false;
        StatusText = string.Empty;
        IsVisible = false;
        RaiseModeStateChanged();
    }

    internal bool TryBuildProfile(
        out GenerationProfile? profile,
        out string? validationError)
    {
        profile = null;

        if (_profileId is not GenerationProfileId profileId)
        {
            validationError = "Open or create a custom generation profile first.";
            return false;
        }

        if (!GenerationSettingsViewModel.TryParseOptionalInt(
            MaxOutputTokensText,
            "Maximum output tokens",
            minimum: 1,
            out int? maxOutputTokens,
            out validationError))
        {
            return false;
        }

        if (!TryParseOptionalDouble(
            TemperatureText,
            "Temperature",
            minimum: 0,
            maximum: null,
            out double? temperature,
            out validationError))
        {
            return false;
        }

        if (!TryParseOptionalDouble(
            TopPText,
            "Top-p",
            minimum: 0,
            maximum: 1,
            out double? topP,
            out validationError))
        {
            return false;
        }

        if (!GenerationSettingsViewModel.TryParseOptionalInt(
            TopKText,
            "Top-k",
            minimum: 0,
            out int? topK,
            out validationError))
        {
            return false;
        }

        if (!GenerationSettingsViewModel.TryParseOptionalInt(
            SeedText,
            "Seed",
            minimum: 0,
            out int? seed,
            out validationError))
        {
            return false;
        }

        bool? reasoningEnabled = ReasoningModeIndex switch
        {
            0 => null,
            1 => false,
            2 => true,
            _ => throw new InvalidOperationException("Unsupported reasoning mode."),
        };
        int? reasoningBudgetTokens = null;

        if (reasoningEnabled == true)
        {
            if (!GenerationSettingsViewModel.TryParseOptionalInt(
                ReasoningBudgetText,
                "Reasoning budget",
                minimum: 1,
                out reasoningBudgetTokens,
                out validationError))
            {
                return false;
            }

            if (reasoningBudgetTokens is null)
            {
                validationError = "Reasoning budget is required when reasoning is enabled.";
                return false;
            }
        }

        string[] suggestions = InitialSuggestionsText
            .Split(_initialSuggestionSeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(value => value.Length > 0)
            .ToArray();

        try
        {
            InferenceGenerationOptions options = new(
                maxOutputTokens,
                temperature,
                topP,
                topK,
                seed,
                reasoningEnabled,
                reasoningBudgetTokens);
            profile = new GenerationProfile(
                profileId,
                Name,
                BaseSystemInstructions,
                options,
                suggestions);
            validationError = null;
            return true;
        }
        catch (ArgumentException exception)
        {
            validationError = exception.Message;
            return false;
        }
    }

    private void LoadProfileFields(GenerationProfile? profile)
    {
        _name = profile?.Name ?? string.Empty;
        _baseSystemInstructions = profile?.BaseSystemInstructions ?? string.Empty;
        _maxOutputTokensText = FormatOptional(profile?.GenerationOptions.MaxOutputTokens);
        _temperatureText = FormatOptional(profile?.GenerationOptions.Temperature);
        _topPText = FormatOptional(profile?.GenerationOptions.TopP);
        _topKText = FormatOptional(profile?.GenerationOptions.TopK);
        _seedText = FormatOptional(profile?.GenerationOptions.Seed);
        _reasoningModeIndex = profile?.GenerationOptions.ReasoningEnabled switch
        {
            true => 2,
            false => 1,
            null => 0,
        };
        _reasoningBudgetText = FormatOptional(profile?.GenerationOptions.ReasoningBudgetTokens);
        if (_reasoningBudgetText.Length == 0)
        {
            _reasoningBudgetText = GenerationSettingsViewModel.DefaultReasoningBudgetTokens
                .ToString(CultureInfo.InvariantCulture);
        }

        _initialSuggestionsText = profile is null
            ? string.Empty
            : string.Join(Environment.NewLine, profile.InitialSuggestions);

        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(BaseSystemInstructions));
        OnPropertyChanged(nameof(MaxOutputTokensText));
        OnPropertyChanged(nameof(TemperatureText));
        OnPropertyChanged(nameof(TopPText));
        OnPropertyChanged(nameof(TopKText));
        OnPropertyChanged(nameof(SeedText));
        OnPropertyChanged(nameof(ReasoningModeIndex));
        OnPropertyChanged(nameof(ReasoningBudgetText));
        OnPropertyChanged(nameof(InitialSuggestionsText));
        OnPropertyChanged(nameof(IsReasoningBudgetEditable));
    }

    private void RaiseModeStateChanged()
    {
        OnPropertyChanged(nameof(IsNewProfile));
        OnPropertyChanged(nameof(IsDraftStale));
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
            NumberStyles.Float,
            CultureInfo.CurrentCulture,
            out double parsed)
            || double.TryParse(
                trimmed,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out parsed);

        if (!parsedSuccessfully || double.IsNaN(parsed) || double.IsInfinity(parsed))
        {
            result = null;
            validationError = $"{label} must be a finite number or left blank for the provider default.";
            return false;
        }

        if (parsed < minimum || (maximum is double upperBound && parsed > upperBound))
        {
            result = null;
            validationError = maximum is double max
                ? $"{label} must be between {minimum} and {max} or left blank for the provider default."
                : $"{label} must be at least {minimum} or left blank for the provider default.";
            return false;
        }

        result = parsed;
        validationError = null;
        return true;
    }

    private static string FormatOptional(int? value)
    {
        return value?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
    }

    private static string FormatOptional(double? value)
    {
        return value?.ToString("G", CultureInfo.InvariantCulture) ?? string.Empty;
    }
}
