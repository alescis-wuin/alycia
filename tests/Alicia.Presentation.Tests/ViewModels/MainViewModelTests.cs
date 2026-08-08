using Alicia.Presentation.ViewModels;

namespace Alicia.Presentation.Tests.ViewModels;

public sealed class MainViewModelTests
{
    [Fact]
    public void DefaultStateIdentifiesAliciaAndUnconfiguredProvider()
    {
        MainViewModel viewModel = new();

        Assert.Equal("Alicia", viewModel.ApplicationName);
        Assert.Equal("Repository foundation ready", viewModel.FoundationStatus);
        Assert.Equal("AI provider not configured", viewModel.ProviderStatus);
    }
}
