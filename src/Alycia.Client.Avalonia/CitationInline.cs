using System;
using Markdig.Helpers;
using Markdig.Parsers;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace Alycia.Client.Avalonia;

internal sealed class CitationInline(string reference) : LeafInline
{
    internal string Reference { get; } = reference;
}

internal sealed class CitationParser : InlineParser
{
    public CitationParser() => OpeningCharacters = ['['];

    public override bool Match(InlineProcessor processor, ref StringSlice slice)
    {
        var span = slice.AsSpan();
        if (span.Length < 15 || span[1] is not ('S' or 's') || span[14] != ']') return false;
        for (int i = 2; i < 14; i++) if (!Uri.IsHexDigit(span[i])) return false;
        var start = processor.GetSourcePosition(slice.Start, out int line, out int column);
        processor.Inline = new CitationInline(span[1..14].ToString())
        { Span = new SourceSpan(start, processor.GetSourcePosition(slice.Start + 14)), Line = line, Column = column };
        slice.Start += 15;
        return true;
    }
}
