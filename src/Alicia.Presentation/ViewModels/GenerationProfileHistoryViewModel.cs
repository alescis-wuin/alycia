using System.Collections.ObjectModel;
using Alicia.Application.Generations;

namespace Alicia.Presentation.ViewModels;

public sealed class GenerationProfileHistoryViewModel : ViewModelBase
{
    private readonly ObservableCollection<GenerationProfileRevisionItemViewModel> _revisions = [];
    private GenerationProfileRevisionItemViewModel? _selectedRevision;
    private bool _hasWorkingDraft;
    private string _statusText = "Select a confirmed custom profile to inspect its revision history.";

    public ObservableCollection<GenerationProfileRevisionItemViewModel> Revisions => _revisions;

    public GenerationProfileRevisionItemViewModel? SelectedRevision
    {
        get => _selectedRevision;
        set => SetProperty(ref _selectedRevision, value);
    }

    public bool HasWorkingDraft
    {
        get => _hasWorkingDraft;
        private set => SetProperty(ref _hasWorkingDraft, value);
    }

    public bool IsVisible => _revisions.Count > 0;

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    internal void Load(
        GenerationProfileCatalog? catalog,
        GenerationProfile? selectedProfile)
    {
        GenerationProfileRevisionId? selectedRevisionId = SelectedRevision?.Id;
        _revisions.Clear();
        SelectedRevision = null;
        HasWorkingDraft = false;

        if (catalog is null
            || selectedProfile is null
            || selectedProfile.IsDefault)
        {
            StatusText = "Select a confirmed custom profile to inspect its revision history.";
            OnPropertyChanged(nameof(IsVisible));
            return;
        }

        List<GenerationProfileRevision> history = [];
        foreach (GenerationProfileRevision revision in catalog.Revisions)
        {
            if (revision.ProfileId == selectedProfile.Id)
            {
                history.Add(revision);
            }
        }

        GenerationProfileRevision? latestRevision = catalog.FindLatestRevision(selectedProfile.Id);
        for (int index = history.Count - 1; index >= 0; index--)
        {
            GenerationProfileRevision revision = history[index];
            _revisions.Add(new GenerationProfileRevisionItemViewModel(
                revision,
                index + 1,
                latestRevision is not null && revision.Id == latestRevision.Id));
        }

        HasWorkingDraft = catalog.FindWorkingDraft(selectedProfile.Id) is not null;
        SelectedRevision = FindRevisionItem(selectedRevisionId)
            ?? (_revisions.Count > 0 ? _revisions[0] : null);
        StatusText = HasWorkingDraft
            ? "A local WorkingDraft already exists. Confirm or discard it before restoring an older revision."
            : history.Count <= 1
                ? "This profile has one confirmed revision. Older revisions will appear here after future saves."
                : "Select an older immutable revision to restore its contents as a new WorkingDraft.";
        OnPropertyChanged(nameof(IsVisible));
    }

    internal void MarkHistoricalRevisionRestored()
    {
        StatusText =
            "Historical contents restored as a WorkingDraft based on the current head. Review it, then save a revision to confirm without rewriting history.";
    }

    internal void SelectCurrentRevision()
    {
        foreach (GenerationProfileRevisionItemViewModel item in _revisions)
        {
            if (item.IsCurrent)
            {
                SelectedRevision = item;
                return;
            }
        }
    }

    private GenerationProfileRevisionItemViewModel? FindRevisionItem(
        GenerationProfileRevisionId? revisionId)
    {
        if (revisionId is null)
        {
            return null;
        }

        foreach (GenerationProfileRevisionItemViewModel item in _revisions)
        {
            if (item.Id == revisionId.Value)
            {
                return item;
            }
        }

        return null;
    }
}
