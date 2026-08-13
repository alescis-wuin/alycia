using System.Collections.ObjectModel;

namespace Alicia.Presentation.ViewModels;

public sealed class ConversationHistoryViewModel : ViewModelBase
{
    private readonly List<ConversationListItemViewModel> _allConversations = [];
    private bool _isConversationHistoryExpanded = true;
    private bool _isHistorySearchBusy;
    private string _historySearchText = string.Empty;

    public ObservableCollection<ConversationListItemViewModel> Conversations { get; } = [];

    internal IReadOnlyList<ConversationListItemViewModel> AllConversations => _allConversations;

    public string HistorySearchText
    {
        get => _historySearchText;
        internal set => SetProperty(ref _historySearchText, value);
    }

    public bool IsHistorySearchBusy
    {
        get => _isHistorySearchBusy;
        internal set => SetProperty(ref _isHistorySearchBusy, value);
    }

    public bool IsConversationHistoryExpanded
    {
        get => _isConversationHistoryExpanded;
        internal set
        {
            if (SetProperty(ref _isConversationHistoryExpanded, value))
            {
                OnPropertyChanged(nameof(IsConversationHistoryCollapsed));
            }
        }
    }

    public bool IsConversationHistoryCollapsed => !IsConversationHistoryExpanded;

    public bool HasConversationHistory => _allConversations.Count > 0;

    internal void ReplaceAll(IEnumerable<ConversationListItemViewModel> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        _allConversations.Clear();
        _allConversations.AddRange(items);
    }

    internal void ApplyVisibleItems(IEnumerable<ConversationListItemViewModel> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        Conversations.Clear();

        foreach (ConversationListItemViewModel item in items)
        {
            Conversations.Add(item);
        }
    }

    internal void CancelAllRenames()
    {
        foreach (ConversationListItemViewModel item in _allConversations)
        {
            item.CancelRename();
        }
    }

    internal void NotifyInteractionStateChanged()
    {
        foreach (ConversationListItemViewModel item in _allConversations)
        {
            item.NotifyInteractionStateChanged();
        }
    }

    internal void ResetPreviews()
    {
        foreach (ConversationListItemViewModel item in _allConversations)
        {
            item.ResetPreview();
        }
    }
}
