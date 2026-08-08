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
        Func<ConversationListItemViewModel, Task> selectAsync)
    {
        ArgumentNullException.ThrowIfNull(summary);
        ArgumentNullException.ThrowIfNull(selectAsync);

        Id = summary.Id;
        Title = summary.Title;
        UpdatedAt = summary.UpdatedAt;
        MessageCount = summary.MessageCount;
        UpdatedAtLabel = summary.UpdatedAt
            .ToLocalTime()
            .ToString("g", CultureInfo.CurrentCulture);
        SelectCommand = new AsyncRelayCommand(() => selectAsync(this));
    }

    public ConversationId Id { get; }

    public string Title { get; }

    public DateTimeOffset UpdatedAt { get; }

    public int MessageCount { get; }

    public string UpdatedAtLabel { get; }

    public IAsyncRelayCommand SelectCommand { get; }

    public bool IsSelected
    {
        get => _isSelected;
        private set => SetProperty(ref _isSelected, value);
    }

    internal void SetSelected(bool isSelected)
    {
        IsSelected = isSelected;
    }
}
