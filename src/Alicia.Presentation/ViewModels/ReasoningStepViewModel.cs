namespace Alicia.Presentation.ViewModels;

public sealed class ReasoningStepViewModel : ViewModelBase
{
    private string _content;

    public ReasoningStepViewModel(string content)
    {
        ArgumentException.ThrowIfNullOrEmpty(content);
        _content = content;
    }

    public string Content
    {
        get => _content;
        private set => SetProperty(ref _content, value);
    }

    internal void Append(string textDelta)
    {
        ArgumentException.ThrowIfNullOrEmpty(textDelta);
        Content += textDelta;
    }
}
