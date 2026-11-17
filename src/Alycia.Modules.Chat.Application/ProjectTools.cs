using System.Text.Json;

namespace Alycia.Modules.Chat.Application;

public sealed record ToolFunction(string Name, JsonElement Arguments);
public sealed record ModelToolCall(ToolFunction Function);
public sealed record ProjectTool(string Name, string Description, JsonElement InputSchema);
public sealed record ToolEvidence(string Path, string Sha256, int Offset, int? NextOffset, bool CompleteSource);
public sealed record ProjectToolResult(string Text, bool IsError = false, ToolEvidence? Evidence = null);
public sealed record ToolObservation(int Number, string Name, string Arguments, bool Succeeded, int ResultBytes, ToolEvidence? Evidence = null, bool RepeatSuppressed = false);
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
            {"type":"object","properties":{"fileId":{"type":"string","format":"uuid"},"offset":{"type":"integer","minimum":0},"maxBytes":{"type":"integer","minimum":128,"maximum":8192},"symbol":{"type":"string","minLength":1,"maxLength":200}},"required":["fileId"],"additionalProperties":false}
            """)),
        new("project_search", "Recherche littérale dans les originaux, première occurrence par fichier. Pagination par fichier ; suivre nextOffset même sans résultat.", Schema("""
            {"type":"object","properties":{"query":{"type":"string","minLength":2,"maxLength":120},"offset":{"type":"integer","minimum":0},"limit":{"type":"integer","minimum":1,"maximum":20}},"required":["query"],"additionalProperties":false}
            """))
    ];
    public static object[] OllamaTools() => All.Select(t => (object)new
    { type = "function", function = new { name = t.Name, description = t.Description, parameters = t.InputSchema } }).ToArray();
    private static JsonElement Schema(string json) => JsonSerializer.Deserialize<JsonElement>(json);
}
