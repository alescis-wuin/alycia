using System;
using System.Collections.Generic;

namespace Alycia.Client.Avalonia;

internal readonly record struct CodeSpan(string Text, string? Color = null);

internal static class CodeHighlight
{
    private static readonly HashSet<string> Keywords = new((
        "abstract as async await bool break case catch class const continue decimal default delegate do double else enum event false finally float for foreach from get global if implements in int interface into internal is let lock long namespace new null object override private protected public readonly record return sealed select set static string struct switch this throw true try using var virtual void where while yield " +
        "and assert def del elif except False from import lambda None nonlocal not or pass raise self True with " +
        "begin by create delete distinct drop end exists group having inner insert join left limit on order outer right table union update values varchar when with " +
        "case done elif esac export fi function param process switch then until").Split(' ', StringSplitOptions.RemoveEmptyEntries), StringComparer.OrdinalIgnoreCase);

    internal static IReadOnlyList<CodeSpan> Scan(string? language, string text)
    {
        string name = (language ?? "").Trim().ToLowerInvariant();
        if (text.Length > 100_000 || name is not ("c#" or "cs" or "csharp" or "xml" or "axaml" or "xaml" or "py" or "python"
            or "json" or "yml" or "yaml" or "sql" or "bash" or "sh" or "fish" or "powershell" or "ps1" or "pwsh" or "diff" or "patch"))
            return [new(text)];
        bool xml = name is "xml" or "axaml" or "xaml";
        bool csharp = name is "cs" or "c#" or "csharp";
        bool hash = name is "py" or "python" or "yml" or "yaml" or "bash" or "sh" or "fish" or "powershell" or "ps1" or "pwsh";
        var result = new List<CodeSpan>();
        int plain = 0, i = 0;
        while (i < text.Length)
        {
            int start = i;
            char ch = text[i++];
            string? role = null;
            if (name is "diff" or "patch")
            {
                i = LineEnd(text, i);
                role = ch switch { '+' => "Jade", '-' => "Error", '@' => "Iris", _ => null };
                if (i < text.Length) i++;
            }
            else if (xml && text.AsSpan(start).StartsWith("<!--", StringComparison.Ordinal))
            { i = Through(text, i + 3, "-->"); role = "Muted"; }
            else if ((csharp || name == "sql") && text.AsSpan(start).StartsWith("/*", StringComparison.Ordinal))
            { i = Through(text, i + 1, "*/"); role = "Muted"; }
            else if ((hash && ch == '#') || (csharp && ch == '/' && i < text.Length && text[i] == '/') ||
                (name == "sql" && ch == '-' && i < text.Length && text[i] == '-'))
            { i = LineEnd(text, i); role = "Muted"; }
            else if (ch is '"' or '\'')
            {
                while (i < text.Length)
                {
                    char next = text[i++];
                    if (next == '\\' && i < text.Length) { i++; continue; }
                    if (next == ch) break;
                }
                role = "Jade";
            }
            else if (char.IsDigit(ch))
            { while (i < text.Length && (char.IsDigit(text[i]) || text[i] is '.' or '_')) i++; role = "Amber"; }
            else if (char.IsLetter(ch) || ch == '_')
            {
                while (i < text.Length && (char.IsLetterOrDigit(text[i]) || text[i] == '_')) i++;
                if ((xml && start > 0 && (text[start - 1] is '<' or '/')) || Keywords.Contains(text[start..i])) role = "Iris";
                else if (i < text.Length && (xml && text[i] == '=' || (name is "yaml" or "yml") && text[i] == ':')) role = "Jade";
            }
            if (role is null) continue;
            if (start > plain) result.Add(new(text[plain..start]));
            result.Add(new(text[start..i], role));
            plain = i;
            if (result.Count > 2000) break;
        }
        if (plain < text.Length || result.Count == 0) result.Add(new(text[plain..]));
        return result;
    }

    private static int LineEnd(string text, int start)
    {
        int end = text.IndexOf('\n', start);
        return end < 0 ? text.Length : end;
    }

    private static int Through(string text, int start, string close)
    {
        int end = text.IndexOf(close, start, StringComparison.Ordinal);
        return end < 0 ? text.Length : end + close.Length;
    }
}
