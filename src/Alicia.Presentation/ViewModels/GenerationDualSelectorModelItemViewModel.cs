using Alicia.Application.Generations;

namespace Alicia.Presentation.ViewModels;

public sealed class GenerationDualSelectorModelItemViewModel : ViewModelBase
{
    private bool _isBoundToActiveBranch;
    private bool _isCatalogPersisted;

    internal GenerationDualSelectorModelItemViewModel(
        GenerationProfileModelScope scope,
        GenerationProfileCatalog catalog,
        bool isCatalogPersisted,
        bool isSavedInLibrary,
        bool isCurrentSavedModel,
        bool isBoundToActiveBranch)
    {
        if (scope.IsEmpty)
        {
            throw new ArgumentException(
                "Generation model scope cannot be empty.",
                nameof(scope));
        }

        ArgumentNullException.ThrowIfNull(catalog);

        Scope = scope;
        Catalog = catalog;
        _isCatalogPersisted = isCatalogPersisted;
        IsSavedInLibrary = isSavedInLibrary;
        IsCurrentSavedModel = isCurrentSavedModel;
        _isBoundToActiveBranch = isBoundToActiveBranch;
    }

    internal GenerationProfileModelScope Scope { get; }

    internal GenerationProfileCatalog Catalog { get; }

    internal bool IsCatalogPersisted => _isCatalogPersisted;

    public string ProviderId => Scope.ProviderId;

    public string ModelReference => Scope.ModelReference;

    public string DisplayName => FormatModelDisplayName(ModelReference);

    public bool IsSavedInLibrary { get; }

    public bool IsCurrentSavedModel { get; }

    public bool IsBoundToActiveBranch
    {
        get => _isBoundToActiveBranch;
        private set
        {
            if (SetProperty(ref _isBoundToActiveBranch, value))
            {
                OnPropertyChanged(nameof(StateText));
            }
        }
    }

    public string StateText
    {
        get
        {
            List<string> states = [];

            if (IsBoundToActiveBranch)
            {
                states.Add("Active branch");
            }

            if (IsCurrentSavedModel)
            {
                states.Add("Current saved model");
            }

            states.Add(IsSavedInLibrary ? "Library" : "Compatibility entry");
            return string.Join(" • ", states);
        }
    }

    internal void MarkCatalogPersisted()
    {
        _isCatalogPersisted = true;
    }

    internal void SetBoundToActiveBranch(bool value)
    {
        IsBoundToActiveBranch = value;
    }

    internal static string FormatModelDisplayName(string modelReference)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelReference);

        int quantizationSeparatorIndex = modelReference.IndexOf(':');
        string withoutQuantization = quantizationSeparatorIndex >= 0
            ? modelReference[..quantizationSeparatorIndex]
            : modelReference;
        int separatorIndex = withoutQuantization.LastIndexOf('/');
        return separatorIndex >= 0 && separatorIndex < withoutQuantization.Length - 1
            ? withoutQuantization[(separatorIndex + 1)..]
            : withoutQuantization;
    }
}
