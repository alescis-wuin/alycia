using Alycia.Modules.Chat.Application;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Alycia.Modules.Chat.Infrastructure;

public static class CodeStructure
{
    public const int Version = 3;
    public static IReadOnlyList<SourceChunk> Split(Guid id, string path, string sha, string text, CancellationToken ct = default, bool hierarchical = false)
    {
        var source = SourceText.From(text);
        var units = new List<SourceChunk>();
        string? parent = null;
        void Add(int start, int end, string symbol, string kind, string[] references, bool complete = true)
        {
            var whole = complete && end - start <= 3200;
            for (var at = start; at < end;)
            {
                ct.ThrowIfCancellationRequested();
                var until = Math.Min(end, at + 3200);
                if (until < end)
                {
                    var newline = text.LastIndexOf('\n', until - 1, until - at);
                    if (newline > at + 400) until = newline + 1;
                    if (until < text.Length && char.IsLowSurrogate(text[until])) until--;
                }
                var value = text[at..until];
                if (!string.IsNullOrWhiteSpace(value)) units.Add(new(id, path, sha, symbol, kind, at, until,
                    source.Lines.GetLineFromPosition(at).LineNumber + 1,
                    source.Lines.GetLineFromPosition(Math.Max(at, until - 1)).LineNumber + 1,
                    value, references, whole, IndexContext: hierarchical ? $"{Path.GetExtension(path)} · {kind} · {parent} · {symbol}" : null));
                at = until;
            }
        }
        if (path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
        {
            // Parse text only; never load MSBuild projects or execute imported code.
            var tree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview), cancellationToken: ct);
            var root = tree.GetRoot(ct);
            var declarations = root.DescendantNodes().OfType<MemberDeclarationSyntax>()
                .Where(n => n is BaseMethodDeclarationSyntax or BasePropertyDeclarationSyntax or BaseFieldDeclarationSyntax or BaseTypeDeclarationSyntax or DelegateDeclarationSyntax).ToArray();
            foreach (var node in declarations)
            {
                var name = node switch
                {
                    MethodDeclarationSyntax m => m.Identifier.ValueText + m.ParameterList,
                    ConstructorDeclarationSyntax c => c.Identifier.ValueText + c.ParameterList,
                    PropertyDeclarationSyntax p => p.Identifier.ValueText,
                    BaseTypeDeclarationSyntax t => t.Identifier.ValueText,
                    DelegateDeclarationSyntax d => d.Identifier.ValueText,
                    FieldDeclarationSyntax f => string.Join(",", f.Declaration.Variables.Select(v => v.Identifier.ValueText)),
                    _ => node.Kind().ToString()
                };
                var parents = node.Ancestors().OfType<BaseTypeDeclarationSyntax>().Reverse().Select(t => t.Identifier.ValueText);
                var symbol = string.Join(".", parents.Append(name));
                var end = node is TypeDeclarationSyntax type && !type.OpenBraceToken.IsMissing ? type.OpenBraceToken.Span.End : node.Span.End;
                var refs = node.DescendantNodes().OfType<IdentifierNameSyntax>().Select(n => n.Identifier.ValueText)
                    .Distinct(StringComparer.Ordinal).Take(64).ToArray();
                if (hierarchical) parent = string.Join(".", node.Ancestors().OfType<BaseNamespaceDeclarationSyntax>().Reverse().Select(n => n.Name.ToString()));
                Add(node.SpanStart, end, symbol, end < node.Span.End ? "type_header" : node.Kind().ToString(), refs, end == node.Span.End);
            }
            // Cover comments/imports/top-level statements and malformed/incomplete code too.
            parent = null;
            var spans = units.OrderBy(u => u.Start).ToArray();
            var cursor = 0;
            foreach (var chunk in spans)
            {
                if (chunk.Start > cursor) Add(cursor, chunk.Start, "fichier", "text", []);
                cursor = Math.Max(cursor, chunk.End);
            }
            if (cursor < text.Length) Add(cursor, text.Length, "fichier", "text", []);
        }
        else
        {
            // Other languages/documents retain exact line ranges; no false AST guarantee.
            var start = 0; var label = "fichier";
            var headings = new string?[6];
            foreach (var line in source.Lines)
            {
                var value = line.ToString();
                if (hierarchical)
                {
                    var depth = value.TakeWhile(c => c == '#').Count();
                    var heading = depth is >= 1 and <= 6 && value.Length > depth && value[depth] == ' ';
                    var location = value.StartsWith("[", StringComparison.Ordinal) && value.EndsWith(']') && value.Length <= 180;
                    if (heading || location)
                    {
                        Add(start, line.Start, label, "text", []); start = line.Start;
                        label = FileContext.Utf8Prefix(value, 180);
                        if (heading)
                        {
                            Array.Clear(headings, depth - 1, headings.Length - depth + 1);
                            headings[depth - 1] = label.TrimStart('#', ' ');
                        }
                        parent = string.Join(" / ", headings.OfType<string>());
                        continue;
                    }
                }
                if (value.StartsWith("# ", StringComparison.Ordinal) || value.StartsWith("## ", StringComparison.Ordinal)
                    || value.StartsWith("def ", StringComparison.Ordinal) || value.StartsWith("class ", StringComparison.Ordinal))
                {
                    Add(start, line.Start, label, "text", []); start = line.Start; label = value.Length > 180 ? value[..180] : value;
                }
            }
            Add(start, text.Length, label, "text", []);
        }
        return units.OrderBy(u => u.Start).ThenBy(u => u.End).ToArray();
    }
}
