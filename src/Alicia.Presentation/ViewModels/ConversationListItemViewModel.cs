using System.Globalization;
using Alicia.Application.Conversations;
using Alicia.Domain.Conversations;
using CommunityToolkit.Mvvm.Input;

namespace Alicia.Presentation.ViewModels;

public sealed class ConversationListItemViewModel : ViewModelBase
{
    private readonly Func<bool> _canInteract;
    private bool _isRenaming;
    private bool _isSelected;
    private string _renameTitle;

    public ConversationListItemViewModel(
        ConversationSummary summary,
        Func<ConversationListItemViewModel, Task> selectAsync,
        Func<ConversationListItemViewModel, Task> beginRenameAsync,
        Func<ConversationListItemViewModel, Task> saveRenameAsync,
        Func<ConversationListItemViewModel, Task> deleteAsync,
        Func<bool> canInteract)
    {
        ArgumentNullException.ThrowIfNull(summary);
        ArgumentNullException.ThrowIfNull(selectAsync);
        ArgumentNullException.ThrowIfNull(beginRenameAsync);
        ArgumentNullException.ThrowIfNull(saveRenameAsync);
        ArgumentNullException.ThrowIfNull(deleteAsync);
        ArgumentNullException.ThrowIfNull(canInteract);

        _canInteract = canInteract;

        Id = summary.Id;
        Title = summary.Title;
        _renameTitle = summary.Title;
        UpdatedAt = summary.UpdatedAt;
        MessageCount = summary.MessageCount;
        UpdatedAtLabel = summary.UpdatedAt
            .ToLocalTime()
            .ToString("g", CultureInfo.CurrentCulture);
        MessageCountLabel = $"{MessageCount} message{(MessageCount == 1 ? string.Empty : "s")}";
        MetadataLabel = $"{UpdatedAtLabel} • {MessageCountLabel}";

        SelectionAutomationName = $"Open conversation {Title}";
        RenameAutomationName = $"Rename conversation {Title}";
        DeleteAutomationName = $"Delete conversation {Title}";
        RenameInputAutomationName = $"New title for conversation {Title}";
        SaveRenameAutomationName = $"Save new title for conversation {Title}";
        CancelRenameAutomationName = $"Cancel renaming conversation {Title}";

        SelectionToolTip = $"Open “{Title}”";
        RenameToolTip = $"Rename “{Title}”";
        DeleteToolTip = $"Delete “{Title}”";
        RenameInputToolTip = "Type a new title. Enter saves; Escape cancels.";

        SelectCommand = new AsyncRelayCommand(() => selectAsync(this), canInteract);
        RenameCommand = new AsyncRelayCommand(() => beginRenameAsync(this), canInteract);
        SaveRenameCommand = new AsyncRelayCommand(() => saveRenameAsync(this), CanExecuteSaveRename);
        CancelRenameCommand = new RelayCommand(CancelRename);
        DeleteCommand = new AsyncRelayCommand(() => deleteAsync(this), canInteract);
    }

    public ConversationId Id { get; }

    public string Title { get; }

    public DateTimeOffset UpdatedAt { get; }

    public int MessageCount { get; }

    public string UpdatedAtLabel { get; }

    public string MessageCountLabel { get; }

    public string MetadataLabel { get; }

    public string SelectionAutomationName { get; }

    public string RenameAutomationName { get; }

    public string DeleteAutomationName { get; }

    public string RenameInputAutomationName { get; }

    public string SaveRenameAutomationName { get; }

    public string CancelRenameAutomationName { get; }

    public string SelectionToolTip { get; }

    public string RenameToolTip { get; }

    public string DeleteToolTip { get; }

    public string RenameInputToolTip { get; }

    public IAsyncRelayCommand SelectCommand { get; }

    public IAsyncRelayCommand RenameCommand { get; }

    public IAsyncRelayCommand SaveRenameCommand { get; }

    public IRelayCommand CancelRenameCommand { get; }

    public IAsyncRelayCommand DeleteCommand { get; }

    public string RenameTitle
    {
        get => _renameTitle;
        set
        {
            if (SetProperty(ref _renameTitle, value))
            {
                OnPropertyChanged(nameof(CanSaveRename));
                SaveRenameCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public bool IsRenaming
    {
        get => _isRenaming;
        private set
        {
            if (SetProperty(ref _isRenaming, value))
            {
                OnPropertyChanged(nameof(IsTitleVisible));
                OnPropertyChanged(nameof(CanSaveRename));
                SaveRenameCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public bool IsTitleVisible => !IsRenaming;

    public bool CanSaveRename => CanExecuteSaveRename();

    public bool IsSelected
    {
        get => _isSelected;
        private set => SetProperty(ref _isSelected, value);
    }

    internal void BeginRename()
    {
        RenameTitle = Title;
        IsRenaming = true;
    }

    internal void CancelRename()
    {
        IsRenaming = false;
        RenameTitle = Title;
    }

    internal void NotifyInteractionStateChanged()
    {
        SelectCommand.NotifyCanExecuteChanged();
        RenameCommand.NotifyCanExecuteChanged();
        SaveRenameCommand.NotifyCanExecuteChanged();
        DeleteCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanSaveRename));
    }

    internal void SetSelected(bool isSelected)
    {
        IsSelected = isSelected;
    }

    private bool CanExecuteSaveRename()
    {
        if (!IsRenaming || !_canInteract())
        {
            return false;
        }

        string normalizedTitle = RenameTitle.Trim();

        return normalizedTitle.Length > 0
            && normalizedTitle.Length <= Conversation.MaxTitleLength
            && !string.Equals(normalizedTitle, Title, StringComparison.Ordinal);
    }
}
