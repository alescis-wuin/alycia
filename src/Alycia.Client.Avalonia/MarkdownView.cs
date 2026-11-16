using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Markdig;
using Markdig.Extensions.Tables;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using MdBlock = Markdig.Syntax.Block;
using MdInline = Markdig.Syntax.Inlines.Inline;

namespace Alycia.Client.Avalonia;

// Native controls only: no HTML execution, web view, remote image fetch or script.
public sealed class MarkdownView : StackPanel
{
    public static readonly StyledProperty<string> TextProperty = AvaloniaProperty.Register<MarkdownView, string>(nameof(Text), "");
    public static readonly StyledProperty<bool> IsMarkdownProperty = AvaloniaProperty.Register<MarkdownView, bool>(nameof(IsMarkdown), true);
    public static readonly StyledProperty<bool> IsStreamingProperty = AvaloniaProperty.Register<MarkdownView, bool>(nameof(IsStreaming));
    public string Text { get => GetValue(TextProperty); set => SetValue(TextProperty, value); }
    public bool IsMarkdown { get => GetValue(IsMarkdownProperty); set => SetValue(IsMarkdownProperty, value); }
    public bool IsStreaming { get => GetValue(IsStreamingProperty); set => SetValue(IsStreamingProperty, value); }
    public event Action? Rendered;
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder().UsePipeTables()
        .UseEmphasisExtras().UseTaskLists().DisableHtml().Build();
    private static readonly IBrush Ink = Brush.Parse("#E3E9F3");
    private static readonly FontFamily Mono = new("Cascadia Code,Consolas,DejaVu Sans Mono,monospace");
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(120) };
    private readonly List<string> _keys = [];
    private bool _rendering;
    private bool _dirty;
    private bool _queued;

    public MarkdownView()
    {
        Spacing = 12;
        _timer.Tick += async (_, _) => { _timer.Stop(); await RenderAsync(); };
        DetachedFromVisualTree += (_, _) => _timer.Stop();
        AttachedToVisualTree += (_, _) => Schedule();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == TextProperty || change.Property == IsMarkdownProperty || change.Property == IsStreamingProperty)
            Schedule();
    }

    private void Schedule()
    {
        _dirty = true;
        if (_rendering) return;
        // History and the first streamed fragment should appear without waiting for a timer.
        if (!IsStreaming || Children.Count == 0)
        {
            _timer.Stop();
            if (!_queued)
            {
                _queued = true;
                Dispatcher.UIThread.Post(async () => { _queued = false; await RenderAsync(); });
            }
            return;
        }
        if (!_timer.IsEnabled) _timer.Start();
    }

    private async Task RenderAsync()
    {
        if (_rendering) return;
        _rendering = true; _dirty = false;
        var snapshot = Text ?? "";
        var markdown = IsMarkdown;
        try
        {
            var document = markdown ? await Task.Run(() => Markdown.Parse(snapshot, Pipeline)) : null;
            if (markdown != IsMarkdown || snapshot != Text && !(IsStreaming && (Text ?? "").StartsWith(snapshot, StringComparison.Ordinal))) return;
            Apply(snapshot, document);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            // Keep the complete source readable even for pathological Markdown.
            Children.Clear(); _keys.Clear(); Children.Add(Plain(snapshot));
        }
        finally
        {
            _rendering = false;
            if (_dirty) Schedule();
        }
    }

    internal void RenderNow()
    {
        _timer.Stop(); _dirty = false;
        Apply(Text ?? "", IsMarkdown ? Markdown.Parse(Text ?? "", Pipeline) : null);
    }

    private void Apply(string source, MarkdownDocument? document)
    {
        if (document is null)
        {
            Children.Clear(); _keys.Clear(); Children.Add(Plain(source)); Rendered?.Invoke(); return;
        }
        var count = Math.Min(document.Count, 1000);
        for (var index = 0; index < count; index++)
        {
            var block = document[index];
            var raw = source.Substring(block.Span.Start, Math.Min(block.Span.Length, source.Length - block.Span.Start));
            // Reference definitions appended later can change earlier link targets.
            var key = block.GetType().Name + raw + (raw.Contains('[') ? source.Length.ToString() : "");
            if (index < _keys.Count && _keys[index] == key) continue;
            var control = Block(block, source, 0);
            if (index < Children.Count) { Children[index] = control; _keys[index] = key; }
            else { Children.Add(control); _keys.Add(key); }
        }
        while (Children.Count > count) { Children.RemoveAt(Children.Count - 1); _keys.RemoveAt(_keys.Count - 1); }
        if (document.Count > count)
        {
            Children.Add(new Expander { Header = "Réponse très longue · suite en texte brut", Content = Plain(source[document[count].Span.Start..]) });
            _keys.Add("overflow");
        }
        Rendered?.Invoke();
    }

    private Control Block(MdBlock block, string source, int depth)
    {
        if (depth > 16) return Plain(source.Substring(block.Span.Start, block.Span.Length));
        switch (block)
        {
            case CodeBlock code:
                var body = code.Lines.ToString();
                var copy = new Button { Content = "Copier le code", FontSize = 11, Padding = new Thickness(10, 5) };
                copy.Click += async (_, _) =>
                {
                    try
                    {
                        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
                        if (clipboard is null) { copy.Content = "Presse-papiers indisponible"; return; }
                        await clipboard.SetTextAsync(body); copy.Content = "Copié";
                    }
                    catch { copy.Content = "Copie indisponible"; }
                };
                var header = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 0, 0, 8) };
                DockPanel.SetDock(copy, Dock.Right); header.Children.Add(copy);
                header.Children.Add(new TextBlock
                {
                    Text = code is FencedCodeBlock fenced ? fenced.Info : "code",
                    FontSize = 11,
                    Foreground = Brush.Parse("#A8BCD3"),
                    VerticalAlignment = VerticalAlignment.Center
                });
                var codeText = Plain(body); codeText.FontFamily = Mono; codeText.FontSize = 13; codeText.TextWrapping = TextWrapping.NoWrap;
                return new Border
                {
                    Background = Brush.Parse("#0C1420"),
                    CornerRadius = new CornerRadius(10),
                    Padding = new Thickness(14),
                    Child = new StackPanel
                    {
                        Children = { header, new ScrollViewer { Content = codeText,
                        HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled } }
                    }
                };
            case Table table:
                var columns = Math.Max(1, table.ColumnDefinitions.Count);
                if (columns > 20 || table.Count > 100) return Plain(source.Substring(block.Span.Start, block.Span.Length));
                var grid = new Grid { ColumnDefinitions = new ColumnDefinitions(string.Join(",", Enumerable.Repeat("*", columns))), MinWidth = columns * 135 };
                for (var rowIndex = 0; rowIndex < table.Count; rowIndex++)
                {
                    grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
                    if (table[rowIndex] is not TableRow row) continue;
                    for (var col = 0; col < Math.Min(columns, row.Count); col++)
                    {
                        var cell = new Border
                        {
                            Padding = new Thickness(10),
                            BorderBrush = Brush.Parse("#35445A"),
                            BorderThickness = new Thickness(0, 0, 1, 1),
                            Background = row.IsHeader ? Brush.Parse("#263348") : Brushes.Transparent,
                            Child = Block(row[col], source, depth + 1)
                        };
                        Grid.SetRow(cell, rowIndex); Grid.SetColumn(cell, col); grid.Children.Add(cell);
                    }
                }
                return new ScrollViewer
                {
                    Content = grid,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Disabled
                };
            case ListBlock list:
                var stack = new StackPanel { Spacing = 7 };
                var number = int.TryParse(list.OrderedStart, out var first) ? first : 1;
                foreach (var item in list)
                {
                    var row = new Grid { ColumnDefinitions = new ColumnDefinitions("32,*"), ColumnSpacing = 6 };
                    row.Children.Add(Plain(list.IsOrdered ? $"{number++}." : "•"));
                    var content = Block(item, source, depth + 1); Grid.SetColumn(content, 1); row.Children.Add(content); stack.Children.Add(row);
                }
                return stack;
            case QuoteBlock quote:
                return new Border
                {
                    BorderBrush = Brush.Parse("#8D9DB7"),
                    BorderThickness = new Thickness(3, 0, 0, 0),
                    Padding = new Thickness(14, 4),
                    Child = Container(quote, source, depth)
                };
            case ThematicBreakBlock:
                return new Border { Height = 1, Background = Brush.Parse("#35445A"), Margin = new Thickness(0, 8) };
            case LeafBlock leaf:
                var text = Plain(leaf.Inline is null ? leaf.Lines.ToString() : "");
                if (leaf.Inline is not null) { text.Inlines = new InlineCollection(); Fill(leaf.Inline, text.Inlines); }
                if (leaf is HeadingBlock heading)
                { text.FontSize = heading.Level switch { 1 => 25, 2 => 21, 3 => 18, _ => 16 }; text.FontWeight = FontWeight.SemiBold; }
                return text;
            case ContainerBlock container: return Container(container, source, depth);
            default: return Plain("");
        }
    }

    private StackPanel Container(ContainerBlock block, string source, int depth)
    {
        var panel = new StackPanel { Spacing = 8 };
        foreach (var child in block) panel.Children.Add(Block(child, source, depth + 1));
        return panel;
    }

    private void Fill(ContainerInline parent, InlineCollection output)
    {
        foreach (var item in parent)
        {
            switch (item)
            {
                case LiteralInline literal: output.Add(new Run(literal.Content.ToString())); break;
                case CodeInline code: output.Add(new Run(code.Content) { FontFamily = Mono, Foreground = Brush.Parse("#E2C38D") }); break;
                case LineBreakInline line: if (line.IsHard) output.Add(new LineBreak()); else output.Add(new Run(" ")); break;
                case EmphasisInline emphasis:
                    var span = new Span();
                    if (emphasis.DelimiterChar == '~') span.TextDecorations = TextDecorations.Strikethrough;
                    else if (emphasis.DelimiterCount >= 2) span.FontWeight = FontWeight.Bold;
                    else span.FontStyle = FontStyle.Italic;
                    Fill(emphasis, span.Inlines); output.Add(span); break;
                case LinkInline link:
                    var label = InlineText(link);
                    if (!link.IsImage && SafeLink(link.Url) is { } uri)
                    {
                        var button = new HyperlinkButton
                        {
                            Content = label.Length == 0 ? link.Url : label,
                            NavigateUri = uri,
                            Padding = new Thickness(0),
                            FontSize = 15,
                            Foreground = Brush.Parse("#B7ACFF")
                        };
                        ToolTip.SetTip(button, uri.AbsoluteUri); output.Add(new InlineUIContainer { Child = button });
                    }
                    else output.Add(new Run(link.IsImage ? $"[Image : {label} · {link.Url}]" : $"{label} ({link.Url})"));
                    break;
                case AutolinkInline auto:
                    if (SafeLink(auto.Url) is { } target) output.Add(new InlineUIContainer
                    {
                        Child = new HyperlinkButton
                        { Content = auto.Url, NavigateUri = target, Padding = new Thickness(0), FontSize = 15 }
                    });
                    else output.Add(new Run(auto.Url));
                    break;
                case Markdig.Extensions.TaskLists.TaskList task: output.Add(new Run(task.Checked ? "☑ " : "☐ ")); break;
                case ContainerInline nested: Fill(nested, output); break;
            }
        }
    }

    private static string InlineText(ContainerInline inline)
    {
        var text = new StringBuilder();
        foreach (var child in inline)
            text.Append(child switch
            {
                LiteralInline literal => literal.Content.ToString(),
                CodeInline code => code.Content,
                ContainerInline nested => InlineText(nested),
                _ => ""
            });
        return text.ToString();
    }

    internal static Uri? SafeLink(string? value) => Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && uri.Scheme is "http" or "https" && uri.UserInfo.Length == 0 ? uri : null;
    private static SelectableTextBlock Plain(string text) => new()
    {
        Text = text,
        TextWrapping = TextWrapping.Wrap,
        FontSize = 15,
        LineHeight = 24,
        Foreground = Ink
    };
}
