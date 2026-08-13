using System.Globalization;
using Alicia.Application.Providers;

namespace Alicia.Presentation.ViewModels;

public sealed class GenerationSettingsViewModel : ViewModelBase
{
    public const int DefaultReasoningBudgetTokens = 512;

    private string _providerMaxOutputTokensText = string.Empty;
    private string _providerTemperatureText = string.Empty;
    private string _providerTopPText = string.Empty;
    private string _providerTopKText = string.Empty;
    private string _providerSeedText = string.Empty;
    private bool _providerReasoningEnabled;
    private string _providerReasoningBudgetText = DefaultReasoningBudgetTokens.ToString(
        CultureInfo.InvariantCulture);

    public string ProviderMaxOutputTokensText
    {
        get => _providerMaxOutputTokensText;
        internal set => SetProperty(ref _providerMaxOutputTokensText, value);
    }

    public string ProviderTemperatureText
    {
        get => _providerTemperatureText;
        internal set => SetProperty(ref _providerTemperatureText, value);
    }

    public string ProviderTopPText
    {
        get => _providerTopPText;
        internal set => SetProperty(ref _providerTopPText, value);
    }

    public string ProviderTopKText
    {
        get => _providerTopKText;
        internal set => SetProperty(ref _providerTopKText, value);
    }

    public string ProviderSeedText
    {
        get => _providerSeedText;
        internal set => SetProperty(ref _providerSeedText, value);
    }

    public bool ProviderReasoningEnabled
    {
        get => _providerReasoningEnabled;
        internal set => SetProperty(ref _providerReasoningEnabled, value);
    }

    public string ProviderReasoningBudgetText
    {
        get => _providerReasoningBudgetText;
        internal set => SetProperty(ref _providerReasoningBudgetText, value);
    }

    internal void Load(InferenceGenerationOptions? configuration)
    {
        _providerMaxOutputTokensText = FormatOptional(configuration?.MaxOutputTokens);
        _providerTemperatureText = FormatOptional(configuration?.Temperature);
        _providerTopPText = FormatOptional(configuration?.TopP);
        _providerTopKText = FormatOptional(configuration?.TopK);
        _providerSeedText = FormatOptional(configuration?.Seed);
        _providerReasoningEnabled = configuration?.ReasoningEnabled == true;
        _providerReasoningBudgetText = FormatOptional(configuration?.ReasoningBudgetTokens);

        if (_providerReasoningBudgetText.Length == 0)
        {
            _providerReasoningBudgetText = DefaultReasoningBudgetTokens.ToString(
                CultureInfo.InvariantCulture);
        }

        OnPropertyChanged(nameof(ProviderMaxOutputTokensText));
        OnPropertyChanged(nameof(ProviderTemperatureText));
        OnPropertyChanged(nameof(ProviderTopPText));
        OnPropertyChanged(nameof(ProviderTopKText));
        OnPropertyChanged(nameof(ProviderSeedText));
        OnPropertyChanged(nameof(ProviderReasoningEnabled));
        OnPropertyChanged(nameof(ProviderReasoningBudgetText));
    }

    internal bool TryBuild(
        out InferenceGenerationOptions? configuration,
        out string? validationError)
    {
        configuration = null;

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

        int? reasoningBudgetTokens = null;

        if (ProviderReasoningEnabled)
        {
            if (!TryParseOptionalInt(
                ProviderReasoningBudgetText,
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

        try
        {
            configuration = new InferenceGenerationOptions(
                maxOutputTokens,
                temperature,
                topP,
                topK,
                seed,
                ProviderReasoningEnabled,
                reasoningBudgetTokens);
            validationError = null;
            return true;
        }
        catch (ArgumentException exception)
        {
            validationError = exception.Message;
            return false;
        }
    }

    internal static bool TryParseOptionalInt(
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
            NumberStyles.Integer,
            CultureInfo.CurrentCulture,
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

    internal static string FormatOptional(int? value)
    {
        return value?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
    }

    private static string FormatOptional(double? value)
    {
        return value?.ToString("G", CultureInfo.InvariantCulture) ?? string.Empty;
    }
}
