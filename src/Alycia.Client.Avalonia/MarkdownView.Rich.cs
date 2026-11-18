using System;
using System.Linq;
using Alycia.Modules.Chat.Application;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Markdig;
using Markdig.Helpers;
using Markdig.Syntax;

namespace Alycia.Client.Avalonia;

public sealed class CitationEventArgs(ChatMessageItem message, SuppliedSource source) : EventArgs
{
    public ChatMessageItem Message { get; } = message;
    public SuppliedSource Source { get; } = source;
}

public sealed partial class MarkdownView
{
    public event EventHandler<CitationEventArgs>? CitationRequested;

    private static MarkdownPipeline CreatePipeline()
    {
        var builder = new MarkdownPipelineBuilder().UsePipeTables().UseEmphasisExtras().UseTaskLists()
            .UseEmojiAndSmiley(enableSmileys: false).UseAutoLinks().UseFootnotes().UseMathematics().DisableHtml();
        builder.InlineParsers.Insert(0, new CitationParser());
        return builder.Build();
    }

    private Control CodePanel(CodeBlock code)
    {
        string body = code.Lines.ToString();
        if (code.Lines.Count > 0)
        {
            var last = code.Lines.Lines[code.Lines.Count - 1].Slice;
            var suffix = last.Text.AsSpan(last.End + 1);
            if (suffix.StartsWith("\r\n", StringComparison.Ordinal)) body += "\r\n";
            else if (!suffix.IsEmpty && suffix[0] is '\n' or '\r') body += suffix[0];
        }
        string language = (code as FencedCodeBlock)?.Info?.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
        var title = new TextBlock { Text = language.Length == 0 ? "Code" : language, FontSize = 14,
            Foreground = Ink("Muted"), VerticalAlignment = VerticalAlignment.Center };
        var copy = CopyControl("Copier le code", body);
        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
        DockPanel.SetDock(copy, Dock.Right);
        header.Children.Add(copy);
        header.Children.Add(title);
        var codeText = Plain(body);
        codeText.FontFamily = Appearance.Mono;
        codeText.FontSize = Math.Max(15, ReadingSize - 2);
        codeText.LineHeight = Math.Ceiling(codeText.FontSize * 1.48);
        codeText.TextWrapping = WrapCode ? TextWrapping.Wrap : TextWrapping.NoWrap;
        var spans = CodeHighlight.Scan(language, body);
        if (spans.Any(s => s.Color is not null))
        {
            codeText.Text = "";
            codeText.Inlines = new InlineCollection();
            foreach (var span in spans) codeText.Inlines.Add(new Run(span.Text) { Foreground = span.Color is null ? Ink("Ink") : Ink(span.Color) });
        }
        return new Border { Background = Ink("Code"), CornerRadius = new CornerRadius(10), Padding = new Thickness(14),
            Child = new StackPanel { Children = { header, new ScrollViewer { Content = codeText,
                HorizontalScrollBarVisibility = WrapCode ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Disabled } } } };
    }

    private Button CopyControl(string label, string source)
    {
        var button = new Button { Content = label, FontSize = 14 };
        AutomationProperties.SetName(button, label);
        ToolTip.SetTip(button, label);
        button.Click += async (_, _) =>
        {
            try
            {
                var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
                if (clipboard is null) { button.Content = "Copie indisponible"; return; }
                await clipboard.SetTextAsync(source);
                button.Content = "Copié";
            }
            catch (Exception) { button.Content = "Copie indisponible"; }
        };
        return button;
    }

    private Control Formula(string tex, bool inline)
    {
        var formula = FormulaView.Create(tex, ReadingSize, Ink("Ink"));
        if (inline)
        {
            var button = new Button { Content = formula, Background = Brushes.Transparent,
                BorderThickness = new Thickness(0), Padding = new Thickness(2, 0) };
            AutomationProperties.SetName(button, "Copier le LaTeX " + tex);
            ToolTip.SetTip(button, "Copier le LaTeX");
            button.Click += async (_, _) =>
            {
                try { if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard) await clipboard.SetTextAsync(tex); }
                catch (Exception) { ToolTip.SetTip(button, "Copie indisponible"); }
            };
            return button;
        }
        return new StackPanel { Spacing = 5, Children = { new ScrollViewer { Content = formula,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled },
            CopyControl("Copier le LaTeX", tex) } };
    }

    private void AddCitation(InlineCollection output, string reference)
    {
        string marker = "[" + reference + "]";
        if (DataContext is ChatMessageItem message &&
            TurnDiagnostics.Read(message.DiagnosticsJson)?.Citations?.Supplied.FirstOrDefault(
                s => s.Reference.Equals(reference, StringComparison.OrdinalIgnoreCase)) is { } source)
        {
            var button = new Button { Content = marker, Foreground = Ink("Jade"), Padding = new Thickness(3, 0) };
            ToolTip.SetTip(button, source.Path);
            AutomationProperties.SetName(button, "Ouvrir la source " + source.Path);
            button.Click += (_, _) => CitationRequested?.Invoke(this, new(message, source));
            output.Add(new InlineUIContainer { Child = button });
        }
        else output.Add(new Run(marker));
    }
}
