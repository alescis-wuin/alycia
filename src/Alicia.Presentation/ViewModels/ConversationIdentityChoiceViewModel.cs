using CommunityToolkit.Mvvm.Input;

namespace Alicia.Presentation.ViewModels;

public sealed class ConversationIdentityChoiceViewModel : ViewModelBase
{
    private bool _isSelected;

    public ConversationIdentityChoiceViewModel(
        string label,
        Func<Task> selectAsync,
        Func<bool> canSelect)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        ArgumentNullException.ThrowIfNull(selectAsync);
        ArgumentNullException.ThrowIfNull(canSelect);

        Label = label;
        SelectCommand = new AsyncRelayCommand(selectAsync, canSelect);
    }

    public string Label { get; }

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

    internal void NotifyCanSelectChanged()
    {
        SelectCommand.NotifyCanExecuteChanged();
    }
}
