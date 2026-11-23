using System.Text;
using System.Text.Json;
using Alycia.Contracts.Chat;
using Alycia.Contracts.Documents;
using Alycia.Contracts.Indexing;
using Alycia.Contracts.Tools;
using static Alycia.Contracts.Tools.ToolArguments;

namespace Alycia.Modules.Tools;

public sealed class SourceCommands(IAuthorizedToolSources sources) : ISourceCommands
{
    public Task<ProjectToolResult> ExecuteAsync(Guid conversation, string name, JsonElement arguments, int bytes, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        Validate(name, arguments, bytes);
        arguments = arguments.Clone();
        return name switch
        {
            "project_list" => ListAsync(conversation, arguments, bytes, ct),
            "project_read" => ReadAsync(conversation, arguments, bytes, ct),
            "project_search" => SearchAsync(conversation, arguments, bytes, ct),
            _ => throw new ArgumentException("Outil inconnu.")
        };
    }

    private async Task<ProjectToolResult> ListAsync(Guid id, JsonElement args, int budget, CancellationToken ct)
    {
        var offset = Int(args, "offset", 0, 0, int.MaxValue);
        var limit = Int(args, "limit", 20, 1, 50);
        var codeOnly = false;
        if (args.TryGetProperty("codeOnly", out var code))
        {
            if (code.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) throw new ArgumentException("codeOnly doit être un booléen.");
            codeOnly = code.GetBoolean();
        }
        var files = (await sources.EntriesAsync(id, ct)).Where(f => !codeOnly || SourceFileType.IsCode(f.Path)).ToArray();
        var page = new List<object>();
        string Result() => Json(new
        {
            total = files.Length,
            offset,
            nextOffset = offset + page.Count < files.Length ? (int?)(offset + page.Count) : null,
            files = page
        });
        foreach (var file in files.Skip(offset).Take(limit))
        {
            page.Add(new
            {
                fileId = file.Id,
                path = Utf8Text.Prefix(file.Path, 320),
                file.Bytes,
                code = SourceFileType.IsCode(file.Path),
                file.Readable,
                mode = file.Mode,
                status = file.Status
            });
            if (Bytes(Result()) <= budget) continue;
            page.RemoveAt(page.Count - 1); break;
        }
        if (offset < files.Length && page.Count == 0) throw new ArgumentException("Budget insuffisant pour l’inventaire.");
        return new(Result());
    }

    private async Task<ProjectToolResult> ReadAsync(Guid conversation, JsonElement args, int budget, CancellationToken ct)
    {
        if (!args.TryGetProperty("fileId", out var key) || key.ValueKind != JsonValueKind.String || !key.TryGetGuid(out var id))
            throw new ArgumentException("fileId doit être l’identifiant fourni par l’inventaire ou la recherche.");
        AuthorizedToolSource file;
        try { file = await sources.ReadAsync(conversation, id, ct); }
        catch (ArgumentException error)
        { return new(Json(new { error = error.Message }), true) { Refusal = ToolRefusal.UnavailableSource }; }
        if (file.Text.Length == 0) return new(Json(new { error = "Aucun texte disponible : " + file.Origin }), true) { Refusal = ToolRefusal.UnavailableSource };
        var start = 0; var end = file.Text.Length; string? symbol = null; var headerOnly = false;
        if (args.TryGetProperty("symbol", out var requested))
        {
            if (requested.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(requested.GetString()) || requested.GetString()!.Length > 200)
                throw new ArgumentException("symbol doit contenir de 1 à 200 caractères.");
            symbol = requested.GetString()!;
            var declarations = sources.Declarations(file, ct).Where(c => c.Kind != "text")
                .GroupBy(c => c.Symbol).ToArray();
            var matches = declarations.Where(g => g.Key.Equals(symbol, StringComparison.Ordinal)).ToArray();
            if (matches.Length == 0) matches = declarations.Where(g => g.Key.Contains(symbol, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (matches.Length != 1)
            {
                var candidates = (matches.Length == 0 ? declarations : matches).Select(g => new
                {
                    symbol = Utf8Text.Prefix(g.Key, 180),
                    startLine = g.First().StartLine,
                    offset = g.First().Start
                }).Take(12).ToList();
                string Choices() => Json(new { error = matches.Length == 0 ? "Symbole introuvable." : "Symbole ambigu : utilisez un nom complet ou un offset.", candidates });
                while (Bytes(Choices()) > budget && candidates.Count > 0) candidates.RemoveAt(candidates.Count - 1);
                return new(Choices(), true) { Refusal = ToolRefusal.InvalidArguments };
            }
            symbol = matches[0].Key; start = matches[0].Min(c => c.Start); end = matches[0].Max(c => c.End);
            headerOnly = matches[0].Any(c => c.Kind == "type_header");
        }
        var offset = Int(args, "offset", start, start, end);
        if (offset < file.Text.Length && char.IsLowSurrogate(file.Text[offset]))
            throw new ArgumentException("Offset au milieu d’un caractère Unicode ; utilisez nextOffset.");
        var size = Int(args, "maxBytes", Math.Min(8192, budget / 2), 128, 131072);
        while (true)
        {
            var text = Utf8Text.Prefix(file.Text[offset..end], size);
            int? next = offset + text.Length < end ? offset + text.Length : null;
            var actualEnd = offset + text.Length;
            var startLine = 1 + file.Text.AsSpan(0, offset).Count('\n');
            var endLine = startLine + file.Text.AsSpan(offset, Math.Max(0, text.Length - 1)).Count('\n');
            var reference = SourceCoordinates.Reference(id, file.Sha256, offset, actualEnd);
            var json = Json(new
            {
                reference,
                fileId = id,
                path = Utf8Text.Prefix(file.Name, 320),
                file.Sha256,
                offset,
                nextOffset = next,
                totalCharacters = file.Text.Length,
                symbol,
                scopeStart = start,
                scopeEnd = end,
                headerOnly,
                startLine,
                endLine,
                end = actualEnd,
                completeSource = file.Complete,
                source = file.Origin,
                text
            });
            if (Bytes(json) <= budget) return new(json, Evidence: new(file.Name, file.Sha256, offset, next, file.Complete, reference, id, actualEnd, startLine, endLine));
            if (size <= 32) throw new ArgumentException("Budget insuffisant pour cette source.");
            size /= 2;
        }
    }

    private async Task<ProjectToolResult> SearchAsync(Guid conversation, JsonElement args, int budget, CancellationToken ct)
    {
        if (!args.TryGetProperty("query", out var q) || q.ValueKind != JsonValueKind.String)
            throw new ArgumentException("query doit être une chaîne littérale.");
        var query = q.GetString()!;
        if (query.Trim().Length < 2 || query.Length > 120 || query.Any(char.IsControl)) throw new ArgumentException("Recherche de 2 à 120 caractères sans contrôle attendue.");
        var offset = Int(args, "offset", 0, 0, int.MaxValue);
        var limit = Int(args, "limit", 10, 1, 20);
        var files = (await sources.EntriesAsync(conversation, ct)).Where(f => f.Readable).ToArray();
        var matches = new List<object>();
        var examined = 0;
        string Result() => Json(new
        {
            totalFiles = files.Length,
            examined,
            offset,
            nextOffset = offset + examined < files.Length ? (int?)(offset + examined) : null,
            firstMatchPerFile = true,
            matches
        });
        foreach (var entry in files.Skip(offset).Take(limit))
        {
            ct.ThrowIfCancellationRequested();
            object? match = null;
            try
            {
                var file = await sources.ReadAsync(conversation, entry.Id, ct);
                var position = file.Text.IndexOf(query, StringComparison.OrdinalIgnoreCase);
                if (position >= 0 || file.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
                {
                    var start = Math.Max(0, position - 80);
                    if (start < file.Text.Length && char.IsLowSurrogate(file.Text[start])) start--;
                    match = new
                    {
                        fileId = file.Id,
                        path = Utf8Text.Prefix(file.Name, 240),
                        offset = start,
                        completeSource = file.Complete,
                        excerpt = Utf8Text.Prefix(file.Text[start..], 240)
                    };
                }
            }
            catch (ArgumentException e) { match = new { fileId = entry.Id, error = e.Message }; }
            if (match is not null) matches.Add(match);
            examined++;
            if (Bytes(Result()) <= budget) continue;
            examined--; if (match is not null) matches.RemoveAt(matches.Count - 1); break;
        }
        if (offset < files.Length && examined == 0) throw new ArgumentException("Budget insuffisant pour la recherche.");
        return new(Result());
    }
}
