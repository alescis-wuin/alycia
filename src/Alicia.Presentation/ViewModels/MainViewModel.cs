namespace Alicia.Presentation.ViewModels;

public sealed class MainViewModel : ViewModelBase
{
    public string ApplicationName { get; } = "Alicia";

    public string Headline { get; } = "Your AI workspace";

    public string FoundationStatus { get; } = "Repository foundation ready";

    public string ProviderStatus { get; } = "AI provider not configured";
}
