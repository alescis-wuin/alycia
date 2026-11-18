using System.Text.Json;

namespace Alycia.Modules.Chat.Application;

public sealed record ToolFunction(string Name, JsonElement Arguments);
public sealed record ModelToolCall(ToolFunction Function);
public sealed record ProjectTool(string Name, string Description, JsonElement InputSchema);
public sealed record ToolEvidence(string Path, string Sha256, int Offset, int? NextOffset, bool CompleteSource, string? Reference = null, Guid? FileId = null, int? End = null, int? StartLine = null, int? EndLine = null);
public sealed record ProjectToolResult(string Text, bool IsError = false, ToolEvidence? Evidence = null, IReadOnlyList<ToolEvidence>? EvidenceItems = null, IReadOnlyList<HistoryEvidence>? History = null);
public sealed record ToolObservation(int Number, string Name, string Arguments, bool Succeeded, int ResultBytes, ToolEvidence? Evidence = null, bool RepeatSuppressed = false, string? Error = null,
    IReadOnlyList<ToolEvidence>? EvidenceItems = null, double ElapsedMilliseconds = 0, IReadOnlyList<HistoryEvidence>? History = null,
    int RetainedTokens = 0, int ContextBudgetTokens = 0);
public interface IProjectTools
{
    Task<bool> HasSourcesAsync(Guid conversation, CancellationToken ct);
    Task<ProjectToolResult> CallAsync(Guid conversation, string name, JsonElement arguments, int maxResultBytes, CancellationToken ct);
}

public static class ProjectToolCatalog
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public static readonly IReadOnlyList<ProjectTool> All =
    [
        new("project_list", "Liste récursive paginée des imports. Suivre nextOffset jusqu’à null.", Schema("""
            {"type":"object","properties":{"offset":{"type":"integer","minimum":0},"limit":{"type":"integer","minimum":1,"maximum":50},"codeOnly":{"type":"boolean"}},"additionalProperties":false}
            """)),
        new("project_read", "Lit une source par fileId, avec symbol optionnel (nom complet ou partiel, ambiguïtés signalées). Offset absolu UTF-16 ; suivre nextOffset jusqu’à null dans ce périmètre.", Schema("""
            {"type":"object","properties":{"fileId":{"type":"string","format":"uuid"},"offset":{"type":"integer","minimum":0},"maxBytes":{"type":"integer","minimum":128,"maximum":131072},"symbol":{"type":"string","minLength":1,"maxLength":200}},"required":["fileId"],"additionalProperties":false}
            """)),
        new("project_search", "Recherche littérale dans les originaux, première occurrence par fichier. Pagination par fichier ; suivre nextOffset même sans résultat.", Schema("""
            {"type":"object","properties":{"query":{"type":"string","minLength":2,"maxLength":120},"offset":{"type":"integer","minimum":0},"limit":{"type":"integer","minimum":1,"maximum":20}},"required":["query"],"additionalProperties":false}
            """)),
        new("project_search_passages", "Passages classés BM25, sans score nul. Pagination par résultat.", Schema("""
            {"type":"object","properties":{"query":{"type":"string"},"offset":{"type":"integer"},"limit":{"type":"integer"}},"required":["query"],"additionalProperties":false}
            """)),
        new("project_find_symbols", "Déclarations syntaxiques C# ; query filtre le nom. Pas de résolution sémantique.", Schema("""
            {"type":"object","properties":{"query":{"type":"string"},"offset":{"type":"integer"},"limit":{"type":"integer"}},"required":["query"],"additionalProperties":false}
            """)),
        new("project_find_references", "Usages syntaxiques C# d’un identifiant exact ; homonymes possibles.", Schema("""
            {"type":"object","properties":{"query":{"type":"string"},"offset":{"type":"integer"},"limit":{"type":"integer"}},"required":["query"],"additionalProperties":false}
            """)),
        new("project_read_many", "Lit 1 à 4 unités par identifiant ; erreurs par entrée et budget global.", Schema("""
            {"type":"object","properties":{"reads":{"type":"array","minItems":1,"maxItems":4,"items":{"type":"object","properties":{"fileId":{"type":"string"},"offset":{"type":"integer"},"symbol":{"type":"string"}},"required":["fileId"],"additionalProperties":false}}},"required":["reads"],"additionalProperties":false}
            """)),
        new("history_search", "Messages anciens classés BM25. Suivre nextOffset puis nextBefore, même sans résultat.", Schema("""
            {"type":"object","properties":{"query":{"type":"string"},"before":{"type":"integer"},"offset":{"type":"integer"},"limit":{"type":"integer"}},"required":["query"],"additionalProperties":false}
            """)),
        new("history_read", "Paire utilisateur/assistant d’une séquence. Offsets séparés pour lire la suite sans perdre les rôles.", Schema("""
            {"type":"object","properties":{"sequence":{"type":"integer"},"userOffset":{"type":"integer"},"assistantOffset":{"type":"integer"},"maxBytes":{"type":"integer"}},"required":["sequence"],"additionalProperties":false}
            """))
    ];
    public static object[] OllamaTools() => All.Select(t => (object)new
    { type = "function", function = new { name = t.Name, description = t.Description, parameters = t.InputSchema } }).ToArray();
    private static JsonElement Schema(string json) => JsonSerializer.Deserialize<JsonElement>(json);
}

// Executes against the caller’s scoped database transaction.
public interface ISnapshotProjectTools : IProjectTools { }
