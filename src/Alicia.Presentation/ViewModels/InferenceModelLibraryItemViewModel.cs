using System.Globalization;
using Alicia.Application.Providers;

namespace Alicia.Presentation.ViewModels;

public sealed class InferenceModelLibraryItemViewModel : ViewModelBase
{
    private bool _isCurrentSavedModel;
    private bool _hasCurrentSettingsDifference;

    internal InferenceModelLibraryItemViewModel(InferenceModelLibraryEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        Entry = entry;
    }

    internal InferenceModelLibraryEntry Entry { get; }

    public string ProviderId => Entry.ProviderId;

    public string ModelReference => Entry.ModelReference;

    public string DisplayName
    {
        get
        {
            int quantizationSeparatorIndex = ModelReference.IndexOf(':');
            string withoutQuantization = quantizationSeparatorIndex >= 0
                ? ModelReference[..quantizationSeparatorIndex]
                : ModelReference;
            int separatorIndex = withoutQuantization.LastIndexOf('/');
            return separatorIndex >= 0 && separatorIndex < withoutQuantization.Length - 1
                ? withoutQuantization[(separatorIndex + 1)..]
                : withoutQuantization;
        }
    }

    public string QuantizationText
    {
        get
        {
            int separatorIndex = ModelReference.IndexOf(':');
            return separatorIndex >= 0 && separatorIndex < ModelReference.Length - 1
                ? ModelReference[(separatorIndex + 1)..]
                : "Provider/model default";
        }
    }

    public string RuntimeSummaryText => Entry.Configuration.ContextSize is int contextSize
        ? $"Context {contextSize} tokens"
        : "Context uses model/provider default";

    public string SavedAtText => "Saved " + Entry.SavedAtUtc.ToString(
        "yyyy-MM-dd HH:mm 'UTC'",
        CultureInfo.InvariantCulture);

    public bool IsCurrentSavedModel
    {
        get => _isCurrentSavedModel;
        private set
        {
            if (SetProperty(ref _isCurrentSavedModel, value))
            {
                OnPropertyChanged(nameof(StateText));
            }
        }
    }

    public string StateText => IsCurrentSavedModel
        ? _hasCurrentSettingsDifference
            ? "Current model • library settings differ"
            : "Current saved model"
        : "Saved in library";

    internal void RefreshCurrentState(InferenceProviderConfiguration? currentConfiguration)
    {
        bool isCurrent = currentConfiguration is not null
            && string.Equals(currentConfiguration.ProviderId, ProviderId, StringComparison.Ordinal)
            && string.Equals(currentConfiguration.ModelReference, ModelReference, StringComparison.Ordinal);
        bool settingsDifference = isCurrent
            && currentConfiguration is not null
            && currentConfiguration.ContextSize != Entry.Configuration.ContextSize;

        _hasCurrentSettingsDifference = settingsDifference;
        IsCurrentSavedModel = isCurrent;
        OnPropertyChanged(nameof(StateText));
    }
}
