using Alicia.Application.Generations;

namespace Alicia.Presentation.ViewModels;

public sealed class GenerationDualSelectorProfileItemViewModel : ViewModelBase
{
    private bool _isBoundToActiveBranch;

    internal GenerationDualSelectorProfileItemViewModel(
        GenerationProfile profile,
        bool hasWorkingDraft,
        bool isBoundToActiveBranch)
    {
        ArgumentNullException.ThrowIfNull(profile);
        Profile = profile;
        HasWorkingDraft = hasWorkingDraft;
        _isBoundToActiveBranch = isBoundToActiveBranch;
    }

    internal GenerationProfile Profile { get; }

    public GenerationProfileId Id => Profile.Id;

    public string Name => Profile.Name;

    public bool IsDefault => Profile.IsDefault;

    public bool HasWorkingDraft { get; }

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

    public string StateText => IsBoundToActiveBranch
        ? HasWorkingDraft
            ? "Active branch • local draft exists"
            : "Active branch"
        : HasWorkingDraft
            ? "Confirmed • local draft exists"
            : IsDefault
                ? "Built-in default"
                : "Confirmed";

    internal void SetBoundToActiveBranch(bool value)
    {
        IsBoundToActiveBranch = value;
    }
}
