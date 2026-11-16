using Alycia.Client.Avalonia;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Media;

namespace Alycia.Client.Avalonia.Tests;

// Run within the existing headless UI test, on the Avalonia dispatcher thread.
internal static class MarkdownChecks
{
    public static void Verify()
    {
        var view = new MarkdownView { Text = "# Titre\n\nUn **texte fort** et `code`.\n\n```python\nprint('bonjour')" };
        view.RenderNow();
        Assert.IsType<SelectableTextBlock>(view.Children[0]);
        Assert.Equal(FontWeight.SemiBold, ((SelectableTextBlock)view.Children[0]).FontWeight);
        Assert.Contains(view.GetLogicalDescendants().OfType<SelectableTextBlock>(), t => t.Text?.Contains("print('bonjour')") == true);
        view.Text += "\n```\n\n| Nom | Valeur |\n|---|---|\n| alpha | 42 |\n\n[site](https://example.com) ![image](https://example.com/image.png)\n\n<script>alert(1)</script>";
        view.RenderNow();
        Assert.Contains(view.GetLogicalDescendants().OfType<Grid>(), g => g.ColumnDefinitions.Count == 2 && g.RowDefinitions.Count == 2);
        Assert.Single(view.GetLogicalDescendants().OfType<HyperlinkButton>());
        Assert.Empty(view.GetLogicalDescendants().OfType<Image>());
        view.IsMarkdown = false; view.RenderNow();
        Assert.Equal(view.Text, Assert.IsType<SelectableTextBlock>(Assert.Single(view.Children)).Text);
    }
}

public sealed class MarkdownLinkTests
{
    [Theory]
    [InlineData("https://example.com", true)]
    [InlineData("http://localhost:8080", true)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("file:///etc/passwd", false)]
    [InlineData("data:text/html,test", false)]
    [InlineData("https://name:password@example.com", false)]
    [InlineData("relative/path", false)]
    public void OnlyExplicitWebNavigationIsEnabled(string url, bool allowed) =>
        Assert.Equal(allowed, MarkdownView.SafeLink(url) is not null);
}
