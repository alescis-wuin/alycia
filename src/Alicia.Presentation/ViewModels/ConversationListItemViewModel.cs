using System.Globalization;
using Alicia.Application.Conversations;
using Alicia.Domain.Conversations;
using CommunityToolkit.Mvvm.Input;

namespace Alicia.Presentation.ViewModels;

public sealed class ConversationListItemViewModel : ViewModelBase
{
    private bool _isSelected;

    public ConversationListItemViewModel(
        ConversationSummary summary,
        Func<ConversationListItemViewModel, Task> selectAsync,
        Func<ConversationListItemViewModel, Task> renameAsync,
        Func<ConversationListItemViewModel, Task> deleteAsync,
        Func<bool> canInteract)
    {
        ArgumentNullException.ThrowIfNull(summary);
        ArgumentNullException.ThrowIfNull(selectAsync);
        ArgumentNullException.ThrowIfNull(renameAsync);
        ArgumentNullException.ThrowIfNull(deleteAsync);
        ArgumentNullException.ThrowIfNull(canInteract);

        Id = summary.Id;
        Title = summary.Title;
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

        SelectCommand = new AsyncRelayCommand(() => selectAsync(this), canInteract);
        RenameCommand = new AsyncRelayCommand(() => renameAsync(this), canInteract);
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

    public IAsyncRelayCommand SelectCommand { get; }

    public IAsyncRelayCommand RenameCommand { get; }

    public IAsyncRelayCommand DeleteCommand { get; }

    public bool IsSelected
    {
        get => _isSelected;
        private set => SetProperty(ref _isSelected, value);
    }

    internal void NotifyInteractionStateChanged()
    {
        SelectCommand.NotifyCanExecuteChanged();
        RenameCommand.NotifyCanExecuteChanged();
        DeleteCommand.NotifyCanExecuteChanged();
    }

    internal void SetSelected(bool isSelected)
    {
        IsSelected = isSelected;
    }
}
