using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Threading;
using Markdig;
using Markdig.Extensions.Tables;
using Markdig.Extensions.Footnotes;
using Markdig.Extensions.Mathematics;
using Avalonia.Automation;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using MdBlock = Markdig.Syntax.Block;
using MdInline = Markdig.Syntax.Inlines.Inline;

namespace Alycia.Client.Avalonia;

// Native controls only: no HTML execution, web view, remote image fetch or script.
public sealed partial class MarkdownView : StackPanel
{
    public static readonly StyledProperty<string> TextProperty = AvaloniaProperty.Register<MarkdownView, string>(nameof(Text), "");
    public static readonly StyledProperty<bool> IsMarkdownProperty = AvaloniaProperty.Register<MarkdownView, bool>(nameof(IsMarkdown), true);
    public static readonly StyledProperty<bool> IsStreamingProperty = AvaloniaProperty.Register<MarkdownView, bool>(nameof(IsStreaming));
    public string Text { get => GetValue(TextProperty); set => SetValue(TextProperty, value); }
    public bool IsMarkdown { get => GetValue(IsMarkdownProperty); set => SetValue(IsMarkdownProperty, value); }
    public bool IsStreaming { get => GetValue(IsStreamingProperty); set => SetValue(IsStreamingProperty, value); }
    public event Action? Rendered;
    public static readonly StyledProperty<double> ReadingSizeProperty = AvaloniaProperty.Register<MarkdownView, double>(nameof(ReadingSize), 18);
    public static readonly StyledProperty<bool> WrapCodeProperty = AvaloniaProperty.Register<MarkdownView, bool>(nameof(WrapCode));
    public double ReadingSize { get => GetValue(ReadingSizeProperty); set => SetValue(ReadingSizeProperty, value); }
    public bool WrapCode { get => GetValue(WrapCodeProperty); set => SetValue(WrapCodeProperty, value); }
    private static readonly MarkdownPipeline Pipeline = CreatePipeline();
    private IBrush Ink(string role) => Appearance.Color(this, role);
    private static readonly FontFamily EmojiFont = new("Noto Color Emoji,Segoe UI Emoji,Apple Color Emoji,Noto Emoji");
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(120) };
    private readonly List<string> _keys = [];
    private bool _rendering;
    private bool _dirty;
    private bool _queued;

    public MarkdownView()
    {
        Spacing = 12;
        ActualThemeVariantChanged += (_, _) => RefreshStyle();
        _timer.Tick += async (_, _) => { _timer.Stop(); await RenderAsync(); };
        DetachedFromVisualTree += (_, _) => _timer.Stop();
        AttachedToVisualTree += (_, _) => Schedule();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (_timer is null) return;
        if (change.Property == ReadingSizeProperty || change.Property == WrapCodeProperty || change.Property == DataContextProperty) RefreshStyle();
        if (change.Property == TextProperty || change.Property == IsMarkdownProperty || change.Property == IsStreamingProperty)
            Schedule();
    }

    private void RefreshStyle()
    {
        _keys.Clear();
        Schedule();
    }

    private static string BlockKind(MdBlock block) => block.GetType().Name;

    private static string Raw(MdBlock block, string source)
    {
        int start = Math.Clamp(block.Span.Start, 0, source.Length);
        int length = Math.Clamp(block.Span.Length, 0, source.Length - start);
        return source.Substring(start, length);
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
        int references = source.Contains("]:", StringComparison.Ordinal) ? StringComparer.Ordinal.GetHashCode(source) : 0;
        for (var index = 0; index < count; index++)
        {
            var block = document[index];
            var raw = Raw(block, source);
            var key = BlockKind(block) + raw + (raw.Contains('[') || block is FootnoteGroup ? references.ToString(CultureInfo.InvariantCulture) : "");
            if (index < _keys.Count && _keys[index] == key) continue;
            var control = Block(block, source, 0);
            if (index < Children.Count) Children[index] = control; else Children.Add(control);
            if (index < _keys.Count) _keys[index] = key; else _keys.Add(key);
        }
        while (Children.Count > count) Children.RemoveAt(Children.Count - 1);
        if (_keys.Count > count) _keys.RemoveRange(count, _keys.Count - count);
        if (document.Count > count)
        {
            Children.Add(new Expander { Header = "Réponse très longue · suite en texte brut", Content = Plain(source[Math.Clamp(document[count].Span.Start, 0, source.Length)..]) });
            _keys.Add("overflow");
        }
        Rendered?.Invoke();
    }

    private Control Block(MdBlock block, string source, int depth)
    {
        if (depth > 16) return Plain(Raw(block, source));
        switch (block)
        {
            case MathBlock math:
                return math.ClosingFencedCharCount < 2 ? Plain("$$\n" + math.Lines.ToString()) : Formula(math.Lines.ToString(), false);
            case CodeBlock code: return CodePanel(code);
            case Footnote note:
                var footnote = Container(note, source, depth);
                footnote.Tag = "footnote:" + note.Order;
                footnote.Children.Insert(0, Plain($"[{note.Order}]"));
                return footnote;
            case Table table:
                var columns = Math.Max(1, table.ColumnDefinitions.Count);
                if (columns > 20 || table.Count > 100) return Plain(Raw(block, source));
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
                            BorderBrush = Ink("Rule"),
                            BorderThickness = new Thickness(0, 0, 1, 1),
                            Background = row.IsHeader ? Ink("Soft") : Brushes.Transparent,
                            Child = Block(row[col], source, depth + 1)
                        };
                        Grid.SetRow(cell, rowIndex); Grid.SetColumn(cell, col); grid.Children.Add(cell);
                        foreach (var cellText in cell.GetLogicalDescendants().OfType<TextBlock>())
                        {
                            cellText.FontSize = Math.Max(15, ReadingSize - 1);
                            if (row.IsHeader) cellText.FontWeight = FontWeight.Bold;
                            cellText.TextAlignment = table.ColumnDefinitions[col].Alignment switch
                            {
                                TableColumnAlign.Center => TextAlignment.Center,
                                TableColumnAlign.Right => TextAlignment.Right,
                                _ => TextAlignment.Left
                            };
                        }
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
                    BorderBrush = Ink("Jade"),
                    BorderThickness = new Thickness(3, 0, 0, 0),
                    Padding = new Thickness(14, 4),
                    Child = Container(quote, source, depth)
                };
            case ThematicBreakBlock:
                return new Border { Height = 1, Background = Ink("Rule"), Margin = new Thickness(0, 8) };
            case LeafBlock leaf:
                var text = Plain(leaf.Inline is null ? leaf.Lines.ToString() : "");
                if (leaf.Inline is not null) { text.Inlines = new InlineCollection(); Fill(leaf.Inline, text.Inlines); }
                if (leaf is HeadingBlock heading)
                {
                    text.FontSize = heading.Level switch { 1 => ReadingSize * 1.55, 2 => ReadingSize * 1.35, _ => ReadingSize * 1.17 };
                    text.LineHeight = Math.Ceiling(text.FontSize * 1.4);
                    text.FontWeight = FontWeight.Bold;
                    text.Margin = new Thickness(0, 4, 0, 4);
                }
                return text;
            case ContainerBlock container: return Container(container, source, depth);
            default: return Plain(Raw(block, source));
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
                case CitationInline citation: AddCitation(output, citation.Reference); break;
                case MathInline math:
                    output.Add(new InlineUIContainer { Child = Formula(math.Content.ToString(), true) });
                    break;
                case FootnoteLink note:
                    if (note.IsBackLink) break;
                    var order = note.Footnote.Order;
                    var jump = new Button { Content = $"[{order}]", Padding = new Thickness(2, 0), Foreground = Ink("Iris") };
                    AutomationProperties.SetName(jump, $"Lire la note {order}");
                    jump.Click += (_, _) => this.GetLogicalDescendants().OfType<StackPanel>()
                        .FirstOrDefault(p => Equals(p.Tag, "footnote:" + order))?.BringIntoView();
                    output.Add(new InlineUIContainer { Child = jump });
                    break;
                case LiteralInline literal: AppendText(output, literal.Content.ToString()); break;
                case HtmlEntityInline entity: AppendText(output, entity.Transcoded.ToString()); break;
                case CodeInline code: output.Add(new Run(code.Content) { FontFamily = Appearance.Mono, Foreground = Ink("Amber") }); break;
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
                            FontSize = ReadingSize,
                            Foreground = Ink("Iris")
                        };
                        ToolTip.SetTip(button, uri.AbsoluteUri); output.Add(new InlineUIContainer { Child = button });
                    }
                    else output.Add(new Run(link.IsImage ? $"[Image : {label} · {link.Url}]" : $"{label} ({link.Url})"));
                    break;
                case AutolinkInline auto:
                    if (SafeLink(auto.Url) is { } target) output.Add(new InlineUIContainer
                    {
                        Child = new HyperlinkButton
                        { Content = auto.Url, NavigateUri = target, Padding = new Thickness(0), FontSize = ReadingSize }
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
                HtmlEntityInline entity => entity.Transcoded.ToString(),
                CodeInline code => code.Content,
                ContainerInline nested => InlineText(nested),
                _ => ""
            });
        return text.ToString();
    }

    private static void AppendText(InlineCollection output, string value)
    {
        if (!value.Any(character => character >= '\u2000')) { output.Add(new Run(value)); return; }
        // Keep each Unicode grapheme intact: ZWJ families, skin tones, flags and keycaps
        // must be shaped together, using the emoji font at normal weight/style.
        var elements = StringInfo.GetTextElementEnumerator(value);
        var buffer = new StringBuilder();
        var emoji = false;
        void Flush()
        {
            if (buffer.Length == 0) return;
            var run = new Run(buffer.ToString());
            if (emoji) { run.FontFamily = EmojiFont; run.FontWeight = FontWeight.Normal; run.FontStyle = FontStyle.Normal; }
            output.Add(run); buffer.Clear();
        }
        while (elements.MoveNext())
        {
            var element = elements.GetTextElement();
            var isEmoji = !element.Contains('\uFE0E') && (element.Contains('\uFE0F') || element.Contains('\u20E3')
                || element.EnumerateRunes().Any(rune => rune.Value is >= 0x1F000 and <= 0x1FAFF or >= 0x2600 and <= 0x27BF));
            if (isEmoji != emoji) { Flush(); emoji = isEmoji; }
            buffer.Append(element);
        }
        Flush();
    }

    internal static Uri? SafeLink(string? value) => Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && uri.Scheme is "http" or "https" && uri.UserInfo.Length == 0 ? uri : null;
    private SelectableTextBlock Plain(string text) => new()
    {
        Text = text,
        TextWrapping = TextWrapping.Wrap,
        FontSize = ReadingSize,
        LineHeight = Math.Ceiling(ReadingSize * 1.6),
        Foreground = Ink("Ink")
    };
}
