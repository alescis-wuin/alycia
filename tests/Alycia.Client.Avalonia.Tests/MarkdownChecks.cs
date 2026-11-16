using Alycia.Client.Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
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
        view.Text = "## :rocket: Développement\n\nA &amp; B &#x1F98A; 👩🏽‍💻 🇫🇷 ❤️ 1️⃣\n\n```text\n:rocket: &amp;\n```";
        view.RenderNow();
        var runs = view.GetLogicalDescendants().OfType<TextBlock>().Where(text => text.Inlines is not null)
            .SelectMany(text => Runs(text.Inlines!)).ToArray();
        var rendered = string.Concat(runs.Select(run => run.Text));
        Assert.Contains("🚀", rendered);
        Assert.Contains("A & B 🦊", rendered);
        foreach (var grapheme in new[] { "👩🏽‍💻", "🇫🇷", "❤️", "1️⃣" })
            Assert.Contains(runs, run => run.Text?.Contains(grapheme) == true && run.FontFamily.Name.Contains("Emoji"));
        Assert.Contains(view.GetLogicalDescendants().OfType<SelectableTextBlock>(), text => text.Text?.Contains(":rocket: &amp;") == true);
        view.Text = "En cours :roc"; view.RenderNow();
        view.Text += "ket:"; view.RenderNow();
        Assert.Contains(view.GetLogicalDescendants().OfType<TextBlock>().Where(text => text.Inlines is not null)
            .SelectMany(text => Runs(text.Inlines!)), run => run.Text?.Contains("🚀") == true);
        view.IsMarkdown = false; view.RenderNow();
        Assert.Equal(view.Text, Assert.IsType<SelectableTextBlock>(Assert.Single(view.Children)).Text);
    }

    private static IEnumerable<Run> Runs(InlineCollection inlines)
    {
        foreach (var inline in inlines)
        {
            if (inline is Run run) yield return run;
            else if (inline is Span span)
                foreach (var child in Runs(span.Inlines)) yield return child;
        }
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
