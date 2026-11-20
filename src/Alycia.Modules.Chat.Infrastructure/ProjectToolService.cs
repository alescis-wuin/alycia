using System.Text;
using System.Text.Json;
using Alycia.Modules.Chat.Application;
using Microsoft.EntityFrameworkCore;

namespace Alycia.Modules.Chat.Infrastructure;

public sealed partial class ProjectToolService(ChatDbContext database, ProjectSourceReader reader, ChatOptions defaults, IProjectIndex? index = null, IIndexedEmbeddings? semantic = null) : ISnapshotProjectTools
{
    public async Task<bool> HasSourcesAsync(Guid id, CancellationToken ct) => await database.ModelSources(id)
        .AnyAsync(f => f.ContextMode != "excluded" && f.Text != "", ct)
        || await database.Messages.AnyAsync(m => m.ConversationId == id, ct);

    public async Task<ProjectToolResult> CallAsync(Guid id, string name, JsonElement arguments, int maxResultBytes, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var conversation = await database.Conversations.AsNoTracking().SingleOrDefaultAsync(c => c.Id == id, ct)
            ?? throw new ConversationNotFoundException();
        try
        {
            if (!ConversationConfiguration.Options(conversation, defaults).UseProjectTools)
                throw new ArgumentException("Les outils sont désactivés dans cette conversation.");
            if (maxResultBytes is < 1024 or > 131072) throw new ArgumentException("Budget de résultat invalide.");
            if (arguments.ValueKind != JsonValueKind.Object || arguments.GetRawText().Length > 4096)
                throw new ArgumentException("Arguments invalides.");
            string[] allowed = name switch
            {
                "project_list" => ["offset", "limit", "codeOnly"],
                "project_read" => ["fileId", "offset", "maxBytes", "symbol"],
                "project_search" => ["query", "offset", "limit"],
                "project_search_passages" or "project_find_symbols" or "project_find_references" => ["query", "offset", "limit"],
                "project_read_many" => ["reads"],
                "history_search" => ["query", "before", "offset", "limit"],
                "history_read" => ["sequence", "userOffset", "assistantOffset", "maxBytes"],
                _ => throw new ArgumentException("Outil inconnu.")
            };
            var keys = arguments.EnumerateObject().Select(p => p.Name).ToArray();
            if (keys.Any(key => !allowed.Contains(key, StringComparer.Ordinal)) || keys.Distinct().Count() != keys.Length)
                throw new ArgumentException("Argument inconnu ou dupliqué.");
            return name switch
            {
                "project_list" => await ListAsync(id, arguments, maxResultBytes, ct),
                "project_read" => await ReadAsync(id, arguments, maxResultBytes, ct),
                "project_read_many" => await ReadManyAsync(id, arguments, maxResultBytes, ct),
                "history_search" or "history_read" => await new HistoryTools(new EfChatRepository(database), semantic,
                    ConversationConfiguration.Options(conversation, defaults)).CallAsync(id, name, arguments, maxResultBytes, ct),
                "project_search_passages" or "project_find_symbols" or "project_find_references" => await ExploreAsync(id, name, arguments, maxResultBytes, ct),
                _ => await SearchAsync(id, arguments, maxResultBytes, ct)
            };
        }
        catch (ArgumentException e) { return new(Json(new { error = e.Message }), true); }
    }

    private async Task<IQueryable<Alycia.Modules.Chat.Domain.Attachment>> SourcesAsync(Guid id, CancellationToken ct)
    {
        var owner = await database.Conversations.AsNoTracking().SingleOrDefaultAsync(c => c.Id == id, ct) ?? throw new ConversationNotFoundException();
        var options = ConversationConfiguration.Options(owner, defaults);
        var sources = database.ModelSources(id);
        if (!options.AgentPlanningEnabled && options.RoutingMode == "off" && !options.VerificationSearchEnabled) return sources;
        var first = Math.Max(1, owner.MessageCount - 2 * (options.AutoSummarize ? options.ProtectedHistoryTurns : options.MaxHistoryTurns) + 1);
        return sources.Where(f => f.ContextMode != "excluded" && f.Text != "" && f.AnchorSequence <= owner.MessageCount + 1 &&
            (f.ContextMode == "pinned" || f.ContextMode == "project" || f.AnchorSequence >= first));
    }
    private sealed record Entry(Guid Id, string Path, long Bytes, string Mode, string Status, bool Readable);
    private async Task<Entry[]> EntriesAsync(Guid id, CancellationToken ct) => await (await SourcesAsync(id, ct)).OrderBy(f => f.Name).ThenBy(f => f.Id)
        .Select(f => new Entry(f.Id, f.Name, f.Size, f.ContextMode, f.Status, f.ContextMode != "excluded" && f.Text != "")).ToArrayAsync(ct);

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
        var files = (await EntriesAsync(id, ct)).Where(f => !codeOnly || ProjectContext.IsCode(f.Path)).ToArray();
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
                path = FileContext.Utf8Prefix(file.Path, 320),
                file.Bytes,
                code = ProjectContext.IsCode(file.Path),
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
        var file = await (await SourcesAsync(conversation, ct)).SingleOrDefaultAsync(f => f.Id == id, ct)
            ?? throw new ArgumentException("Fichier introuvable dans cette conversation.");
        var source = await reader.ReadAsync(file, ct);
        if (source.Text.Length == 0) throw new ArgumentException("Aucun texte disponible : " + source.Origin);
        var start = 0; var end = source.Text.Length; string? symbol = null; var headerOnly = false;
        if (args.TryGetProperty("symbol", out var requested))
        {
            if (requested.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(requested.GetString()) || requested.GetString()!.Length > 200)
                throw new ArgumentException("symbol doit contenir de 1 à 200 caractères.");
            symbol = requested.GetString()!;
            var declarations = CodeStructure.Split(file.Id, file.Name, file.Sha256, source.Text, ct).Where(c => c.Kind != "text")
                .GroupBy(c => c.Symbol).ToArray();
            var matches = declarations.Where(g => g.Key.Equals(symbol, StringComparison.Ordinal)).ToArray();
            if (matches.Length == 0) matches = declarations.Where(g => g.Key.Contains(symbol, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (matches.Length != 1)
            {
                var candidates = (matches.Length == 0 ? declarations : matches).Select(g => new
                {
                    symbol = FileContext.Utf8Prefix(g.Key, 180),
                    startLine = g.First().StartLine,
                    offset = g.First().Start
                }).Take(12).ToList();
                string Choices() => Json(new { error = matches.Length == 0 ? "Symbole introuvable." : "Symbole ambigu : utilisez un nom complet ou un offset.", candidates });
                while (Bytes(Choices()) > budget && candidates.Count > 0) candidates.RemoveAt(candidates.Count - 1);
                return new(Choices(), true);
            }
            symbol = matches[0].Key; start = matches[0].Min(c => c.Start); end = matches[0].Max(c => c.End);
            headerOnly = matches[0].Any(c => c.Kind == "type_header");
        }
        var offset = Int(args, "offset", start, start, end);
        if (offset < source.Text.Length && char.IsLowSurrogate(source.Text[offset]))
            throw new ArgumentException("Offset au milieu d’un caractère Unicode ; utilisez nextOffset.");
        var size = Int(args, "maxBytes", Math.Min(8192, budget / 2), 128, 131072);
        while (true)
        {
            var text = FileContext.Utf8Prefix(source.Text[offset..end], size);
            int? next = offset + text.Length < end ? offset + text.Length : null;
            var actualEnd = offset + text.Length;
            var startLine = 1 + source.Text.AsSpan(0, offset).Count('\n');
            var endLine = startLine + source.Text.AsSpan(offset, Math.Max(0, text.Length - 1)).Count('\n');
            var reference = ProjectRetrieval.Reference(id, file.Sha256, offset, actualEnd);
            var json = Json(new
            {
                reference,
                fileId = id,
                path = FileContext.Utf8Prefix(file.Name, 320),
                file.Sha256,
                offset,
                nextOffset = next,
                totalCharacters = source.Text.Length,
                symbol,
                scopeStart = start,
                scopeEnd = end,
                headerOnly,
                startLine,
                endLine,
                end = actualEnd,
                completeSource = source.Complete,
                source = source.Origin,
                text
            });
            if (Bytes(json) <= budget) return new(json, Evidence: new(file.Name, file.Sha256, offset, next, source.Complete, reference, id, actualEnd, startLine, endLine));
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
        var files = (await EntriesAsync(conversation, ct)).Where(f => f.Readable).ToArray();
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
            var file = await database.ModelSources(conversation).SingleAsync(f => f.Id == entry.Id, ct);
            object? match = null;
            try
            {
                var source = await reader.ReadAsync(file, ct);
                var position = source.Text.IndexOf(query, StringComparison.OrdinalIgnoreCase);
                if (position >= 0 || file.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
                {
                    var start = Math.Max(0, position - 80);
                    if (start < source.Text.Length && char.IsLowSurrogate(source.Text[start])) start--;
                    match = new
                    {
                        fileId = file.Id,
                        path = FileContext.Utf8Prefix(file.Name, 240),
                        offset = start,
                        completeSource = source.Complete,
                        excerpt = FileContext.Utf8Prefix(source.Text[start..], 240)
                    };
                }
            }
            catch (ArgumentException e) { match = new { fileId = file.Id, error = e.Message }; }
            if (match is not null) matches.Add(match);
            examined++;
            if (Bytes(Result()) <= budget) continue;
            examined--; if (match is not null) matches.RemoveAt(matches.Count - 1); break;
        }
        if (offset < files.Length && examined == 0) throw new ArgumentException("Budget insuffisant pour la recherche.");
        return new(Result());
    }
    private static int Int(JsonElement args, string name, int fallback, int min, int max)
    {
        if (!args.TryGetProperty(name, out var value)) return fallback;
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var result) || result < min || result > max)
            throw new ArgumentException($"{name} doit être un entier entre {min} et {max}.");
        return result;
    }
    private static string Json(object value) => JsonSerializer.Serialize(value, ProjectToolCatalog.Json);
    private static int Bytes(string text) => Encoding.UTF8.GetByteCount(text);
}
